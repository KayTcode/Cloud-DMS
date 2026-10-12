using System.Data;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.DeleteFile;

public record DeleteFileCommand(Guid FileId) : IRequest<Result<bool>>;

public class DeleteFileCommandHandler : IRequestHandler<DeleteFileCommand, Result<bool>>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageServiceFactory _storageServiceFactory;
    private readonly ICurrentUserService _currentUserService;

    public DeleteFileCommandHandler(
        IApplicationDbContext context,
        IStorageServiceFactory storageServiceFactory,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _storageServiceFactory = storageServiceFactory;
        _currentUserService = currentUserService;
    }

    public async Task<Result<bool>> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var fileEntry = await _context.FileEntries
            .Include(f => f.StorageProvider)
            .FirstOrDefaultAsync(f => f.Id == request.FileId && !f.IsDeleted, cancellationToken);

        if (fileEntry == null)
        {
            return Result<bool>.Failure("Tệp tin không tồn tại hoặc đã bị xóa.");
        }

        // Chống IDOR: Chỉ SystemAdmin mới được xóa xuyên Tenant, còn lại phải đúng TenantId của Caller
        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        if (!isSystemAdmin)
        {
            if (!_currentUserService.TenantId.HasValue || fileEntry.TenantId != _currentUserService.TenantId.Value)
            {
                return Result<bool>.Failure("Tệp tin không tồn tại hoặc đã bị xóa.");
            }

            // Với Employee: chỉ được xóa file thuộc phòng ban của mình
            if (_currentUserService.Role == "Employee" && _currentUserService.DepartmentId.HasValue)
            {
                if (fileEntry.DepartmentId != _currentUserService.DepartmentId.Value)
                {
                    return Result<bool>.Failure("Tệp tin không tồn tại hoặc đã bị xóa.");
                }
            }
        }

        // Xóa trên Cloud nếu có StorageProvider (Network I/O ngoài transaction để tránh lock DB)
        if (fileEntry.StorageProvider != null)
        {
            try
            {
                var storageService = _storageServiceFactory.CreateService(fileEntry.StorageProvider);
                await storageService.DeleteFileAsync(fileEntry.StorageKey, cancellationToken);
            }
            catch
            {
                // Tiếp tục xử lý xóa logic để đảm bảo giải phóng dung lượng quota
            }
        }

        // Bọc cập nhật hoàn lại quota và đánh dấu xóa trong transaction
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

                if (lockedFile == null || lockedFile.IsDeleted)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<bool>.Failure("Tệp tin không tồn tại hoặc đã bị xóa.");
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

                // Đánh dấu đã xóa
                lockedFile.IsDeleted = true;

                // Ghi AuditLog
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = lockedFile.TenantId,
                    UserId = _currentUserService.UserId ?? lockedFile.OwnerId,
                    Action = "Delete",
                    EntityName = "FileEntry",
                    EntityId = lockedFile.Id,
                    Details = $"Đã xóa tệp tin '{lockedFile.Name}' và giải phóng {lockedFile.SizeBytes} bytes dung lượng.",
                    CreatedAt = DateTime.UtcNow
                });

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

