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

        // Xóa trên Cloud nếu có StorageProvider
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

            // Hoàn lại dung lượng cho StorageProvider
            fileEntry.StorageProvider.UsedCapacityBytes = Math.Max(0, fileEntry.StorageProvider.UsedCapacityBytes - fileEntry.SizeBytes);
        }

        // Hoàn lại dung lượng cho Tenant
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == fileEntry.TenantId, cancellationToken);
        if (tenant != null)
        {
            tenant.StorageUsedBytes = Math.Max(0, tenant.StorageUsedBytes - fileEntry.SizeBytes);
        }

        // Hoàn lại dung lượng cá nhân cho User
        var userQuota = await _context.UserStorageQuotas.FirstOrDefaultAsync(q => q.UserId == fileEntry.OwnerId, cancellationToken);
        if (userQuota != null)
        {
            userQuota.UsedBytes = Math.Max(0, userQuota.UsedBytes - fileEntry.SizeBytes);
        }

        // Đánh dấu đã xóa
        fileEntry.IsDeleted = true;

        // Ghi AuditLog
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = fileEntry.TenantId,
            UserId = _currentUserService.UserId ?? fileEntry.OwnerId,
            Action = "Delete",
            EntityName = "FileEntry",
            EntityId = fileEntry.Id,
            Details = $"Đã xóa tệp tin '{fileEntry.Name}' và giải phóng {fileEntry.SizeBytes} bytes dung lượng.",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
