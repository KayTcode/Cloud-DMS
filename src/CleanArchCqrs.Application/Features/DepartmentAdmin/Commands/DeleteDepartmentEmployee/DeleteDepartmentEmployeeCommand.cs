using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.DeleteDepartmentEmployee;

public record DeleteDepartmentEmployeeCommand : IRequest<Result>
{
    public Guid DepartmentId { get; init; }
    public Guid UserId { get; init; }
    public bool RemoveFromDepartmentOnly { get; init; } = false;
}

public class DeleteDepartmentEmployeeCommandHandler
    : IRequestHandler<DeleteDepartmentEmployeeCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteDepartmentEmployeeCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteDepartmentEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result.Failure($"Phòng ban với ID '{request.DepartmentId}' không tồn tại.");
        }

        // 1. Kiểm tra quyền của người gọi
        var callerRole = _currentUserService.Role;
        var callerDeptId = _currentUserService.DepartmentId;
        var callerTenantId = _currentUserService.TenantId;
        var currentUserId = _currentUserService.UserId;

        if (callerRole == "DepartmentAdmin")
        {
            if (!callerDeptId.HasValue && currentUserId.HasValue)
            {
                var caller = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value, cancellationToken);
                callerDeptId = caller?.DepartmentId;
            }

            if (!callerDeptId.HasValue || callerDeptId.Value != request.DepartmentId)
            {
                return Result.Failure("Bạn chỉ có quyền xóa/vô hiệu hóa nhân viên trong phòng ban của mình.");
            }
        }
        else if (callerRole == "TenantAdmin")
        {
            if (!callerTenantId.HasValue && currentUserId.HasValue)
            {
                var caller = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value, cancellationToken);
                callerTenantId = caller?.TenantId;
            }

            if (!callerTenantId.HasValue || callerTenantId.Value != department.TenantId)
            {
                return Result.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }
        }
        else if (callerRole != "SystemAdmin")
        {
            return Result.Failure("Bạn không có quyền quản lý thành viên của phòng ban này.");
        }

        // 2. Chặn tự xóa chính mình
        if (currentUserId.HasValue && currentUserId.Value == request.UserId)
        {
            return Result.Failure("Bạn không thể tự xóa hoặc vô hiệu hóa tài khoản của chính mình.");
        }

        // 3. Tìm nhân viên mục tiêu
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null || user.DepartmentId != request.DepartmentId)
        {
            return Result.Failure($"Không tìm thấy nhân viên với ID '{request.UserId}' trong phòng ban này.");
        }

        // 4. Bảo vệ: Không cho phép xóa quản trị viên cấp cao hoặc Trưởng phòng khác qua API này
        var targetRoles = user.UserRoles.Select(ur => ur.Role?.Name).Where(n => n != null).ToList();
        if (targetRoles.Contains("SystemAdmin") || targetRoles.Contains("TenantAdmin"))
        {
            return Result.Failure("Không thể xóa hoặc vô hiệu hóa quản trị viên cấp cao.");
        }

        if (targetRoles.Contains("DepartmentAdmin") && callerRole == "DepartmentAdmin")
        {
            return Result.Failure("Không thể xóa hoặc vô hiệu hóa tài khoản của Trưởng phòng.");
        }

        // 5. Thu hồi toàn bộ Refresh Tokens
        var refreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id)
            .ToListAsync(cancellationToken);

        if (refreshTokens.Any())
        {
            _context.RefreshTokens.RemoveRange(refreshTokens);
        }

        // 6. Thực hiện Soft-delete / Vô hiệu hóa hoặc Gỡ khỏi phòng ban
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        if (request.RemoveFromDepartmentOnly)
        {
            user.DepartmentId = null;
        }

        // 7. Giải phóng phần Quota chưa sử dụng để hoàn trả vào Quota khả dụng của phòng ban
        var userQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == user.Id, cancellationToken);

        if (userQuota != null && userQuota.QuotaBytes > userQuota.UsedBytes)
        {
            // Thu hồi dung lượng thừa về mức dung lượng đã thực dùng
            userQuota.QuotaBytes = userQuota.UsedBytes;
        }

        // 8. Ghi AuditLog
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = department.TenantId,
            UserId = currentUserId,
            Action = request.RemoveFromDepartmentOnly ? "RemoveDepartmentEmployee" : "DeactivateDepartmentEmployee",
            EntityName = "User",
            EntityId = user.Id,
            Details = $"{(request.RemoveFromDepartmentOnly ? "Gỡ nhân viên" : "Vô hiệu hóa nhân viên")} '{user.FullName}' khỏi phòng ban '{department.Name}'. Đã thu hồi phiên đăng nhập và hoàn trả Quota khả dụng.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
