using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.RestoreFile;

public record RestoreFileCommand(Guid FileId) : IRequest<Result<bool>>;

public class RestoreFileCommandHandler : IRequestHandler<RestoreFileCommand, Result<bool>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RestoreFileCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<bool>> Handle(RestoreFileCommand request, CancellationToken cancellationToken)
    {
        var fileEntry = await _context.FileEntries
            .FirstOrDefaultAsync(f => f.Id == request.FileId && f.IsDeleted, cancellationToken);

        if (fileEntry == null)
        {
            return Result<bool>.Failure("Tệp tin không tồn tại trong thùng rác hoặc đã được khôi phục.");
        }

        // Chống IDOR: Chỉ SystemAdmin hoặc người trong cùng Tenant mới được khôi phục
        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        if (!isSystemAdmin)
        {
            if (!_currentUserService.TenantId.HasValue || fileEntry.TenantId != _currentUserService.TenantId.Value)
            {
                return Result<bool>.Failure("Tệp tin không tồn tại trong thùng rác hoặc không thuộc quyền quản lý.");
            }

            // Với Employee: chỉ được khôi phục file do mình sở hữu hoặc thuộc phòng ban của mình
            if (_currentUserService.Role == "Employee")
            {
                var isOwner = _currentUserService.UserId.HasValue && fileEntry.OwnerId == _currentUserService.UserId.Value;
                var isSameDept = _currentUserService.DepartmentId.HasValue && fileEntry.DepartmentId == _currentUserService.DepartmentId.Value;
                if (!isOwner && !isSameDept)
                {
                    return Result<bool>.Failure("Bạn không có quyền khôi phục tệp tin này.");
                }
            }

            // Với DepartmentAdmin: chỉ khôi phục file thuộc phòng ban mình
            if (_currentUserService.Role == "DepartmentAdmin" && _currentUserService.DepartmentId.HasValue)
            {
                if (fileEntry.DepartmentId != _currentUserService.DepartmentId.Value)
                {
                    return Result<bool>.Failure("Bạn không có quyền khôi phục tệp tin của phòng ban khác.");
                }
            }
        }

        var callerUserId = _currentUserService.UserId ?? fileEntry.OwnerId;

        // Khôi phục trạng thái hoạt động của file
        fileEntry.IsDeleted = false;
        fileEntry.DeletedAt = null;
        fileEntry.DeletedByUserId = null;

        // Ghi AuditLog
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = fileEntry.TenantId,
            UserId = callerUserId,
            Action = "RestoreFromTrash",
            EntityName = "FileEntry",
            EntityId = fileEntry.Id,
            Details = $"Đã khôi phục tệp tin '{fileEntry.Name}' ({fileEntry.SizeBytes} bytes) từ thùng rác.",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
