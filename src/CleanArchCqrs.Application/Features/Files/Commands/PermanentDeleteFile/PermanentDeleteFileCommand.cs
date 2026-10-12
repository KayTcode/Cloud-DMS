using System.Data;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.PermanentDeleteFile;

public record PermanentDeleteFileCommand(Guid FileId) : IRequest<Result<bool>>;

public class PermanentDeleteFileCommandHandler : IRequestHandler<PermanentDeleteFileCommand, Result<bool>>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageServiceFactory _storageServiceFactory;
    private readonly ICurrentUserService _currentUserService;

    public PermanentDeleteFileCommandHandler(
        IApplicationDbContext context,
        IStorageServiceFactory storageServiceFactory,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _storageServiceFactory = storageServiceFactory;
        _currentUserService = currentUserService;
    }

    public async Task<Result<bool>> Handle(PermanentDeleteFileCommand request, CancellationToken cancellationToken)
    {
        var fileEntry = await _context.FileEntries
            .Include(f => f.StorageProvider)
            .FirstOrDefaultAsync(f => f.Id == request.FileId && f.IsDeleted, cancellationToken);

        if (fileEntry == null)
        {
            return Result<bool>.Failure("Tệp tin không tồn tại trong thùng rác hoặc đã bị xóa vĩnh viễn.");
        }

        // Chống IDOR: Chỉ SystemAdmin hoặc TenantAdmin hoặc Owner của file mới được xóa vĩnh viễn
        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        var isTenantAdmin = _currentUserService.Role == "TenantAdmin";
        if (!isSystemAdmin)
        {
            if (!_currentUserService.TenantId.HasValue || fileEntry.TenantId != _currentUserService.TenantId.Value)
            {
                return Result<bool>.Failure("Tệp tin không tồn tại trong thùng rác hoặc không thuộc quyền quản lý.");
            }

            // Employee chỉ được xóa vĩnh viễn file do mình sở hữu
            if (!isTenantAdmin && _currentUserService.Role == "Employee")
            {
                var isOwner = _currentUserService.UserId.HasValue && fileEntry.OwnerId == _currentUserService.UserId.Value;
                if (!isOwner)
                {
                    return Result<bool>.Failure("Bạn không có quyền xóa vĩnh viễn tệp tin này.");
                }
            }

            // DepartmentAdmin chỉ được xóa vĩnh viễn file thuộc phòng ban mình
            if (_currentUserService.Role == "DepartmentAdmin" && _currentUserService.DepartmentId.HasValue)
            {
                if (fileEntry.DepartmentId != _currentUserService.DepartmentId.Value)
                {
                    return Result<bool>.Failure("Bạn không có quyền xóa vĩnh viễn tệp tin của phòng ban khác.");
                }
            }
        }

        // 1. Xóa file vật lý trên Cloud Storage Provider (ngoài transaction để tránh treo connection)
        if (fileEntry.StorageProvider != null)
        {
            try
            {
                var storageService = _storageServiceFactory.CreateService(fileEntry.StorageProvider);
                await storageService.DeleteFileAsync(fileEntry.StorageKey, cancellationToken);
            }
            catch
            {
                // Vẫn tiếp tục để giải phóng dung lượng quota cho Tenant & User
            }
        }

        // 2. Trừ Quota và xóa bản ghi trong Database dưới Serializable Transaction
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            try
            {
                var lockedFile = await _context.FileEntries
                    .Include(f => f.StorageProvider)
                    .FirstOrDefaultAsync(f => f.Id == fileEntry.Id, cancellationToken);

                if (lockedFile == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<bool>.Failure("Tệp tin đã bị xóa.");
                }

                // Hoàn lại dung lượng cho StorageProvider
                if (lockedFile.StorageProvider != null)
                {
                    lockedFile.StorageProvider.UsedCapacityBytes = Math.Max(0, lockedFile.StorageProvider.UsedCapacityBytes - lockedFile.SizeBytes);
                }

                // Hoàn lại dung lượng cho Tenant
                var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == lockedFile.TenantId, cancellationToken);
                if (tenant != null)
                {
                    tenant.StorageUsedBytes = Math.Max(0, tenant.StorageUsedBytes - lockedFile.SizeBytes);
                }

                // Hoàn lại dung lượng cho Department
                var departmentId = lockedFile.DepartmentId;
                if (!departmentId.HasValue)
                {
                    var fileOwner = await _context.Users.FirstOrDefaultAsync(u => u.Id == lockedFile.OwnerId, cancellationToken);
                    departmentId = fileOwner?.DepartmentId;
                }

                if (departmentId.HasValue)
                {
                    var department = await _context.Departments.FirstOrDefaultAsync(d => d.Id == departmentId.Value, cancellationToken);
                    if (department != null)
                    {
                        department.StorageUsedBytes = Math.Max(0, department.StorageUsedBytes - lockedFile.SizeBytes);
                    }
                }

                // Hoàn lại dung lượng cá nhân cho User
                var userQuota = await _context.UserStorageQuotas.FirstOrDefaultAsync(q => q.UserId == lockedFile.OwnerId, cancellationToken);
                if (userQuota != null)
                {
                    userQuota.UsedBytes = Math.Max(0, userQuota.UsedBytes - lockedFile.SizeBytes);
                }

                var callerUserId = _currentUserService.UserId ?? lockedFile.OwnerId;

                // Ghi AuditLog
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = lockedFile.TenantId,
                    UserId = callerUserId,
                    Action = "PermanentDelete",
                    EntityName = "FileEntry",
                    EntityId = lockedFile.Id,
                    Details = $"Đã xóa vĩnh viễn tệp tin '{lockedFile.Name}' và giải phóng {lockedFile.SizeBytes} bytes dung lượng.",
                    CreatedAt = DateTime.UtcNow
                });

                // Xóa bản ghi file khỏi CSDL
                _context.FileEntries.Remove(lockedFile);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Result<bool>.Success(true);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
