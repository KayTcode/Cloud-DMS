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

        var callerUserId = _currentUserService.UserId ?? fileEntry.OwnerId;

        // Bọc cập nhật đánh dấu xóa mềm trong transaction
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            try
            {
                var lockedFile = await _context.FileEntries
                    .FirstOrDefaultAsync(f => f.Id == fileEntry.Id, cancellationToken);

                if (lockedFile == null || lockedFile.IsDeleted)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<bool>.Failure("Tệp tin không tồn tại hoặc đã bị xóa.");
                }

                // Đánh dấu chuyển vào thùng rác (Không xóa file trên Cloud và Chưa trừ Quota)
                lockedFile.IsDeleted = true;
                lockedFile.DeletedAt = DateTime.UtcNow;
                lockedFile.DeletedByUserId = callerUserId;

                // Ghi AuditLog
                _context.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = lockedFile.TenantId,
                    UserId = callerUserId,
                    Action = "MoveToTrash",
                    EntityName = "FileEntry",
                    EntityId = lockedFile.Id,
                    Details = $"Đã chuyển tệp tin '{lockedFile.Name}' ({lockedFile.SizeBytes} bytes) vào thùng rác.",
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
