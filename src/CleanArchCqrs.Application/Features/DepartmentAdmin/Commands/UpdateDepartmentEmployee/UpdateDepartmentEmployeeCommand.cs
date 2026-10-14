using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.UpdateDepartmentEmployee;

public record UpdateDepartmentEmployeeCommand : IRequest<Result<DepartmentMemberDto>>
{
    public Guid DepartmentId { get; init; }
    public Guid UserId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; } = true;
    public long? StorageQuotaBytes { get; init; }
}

public class UpdateDepartmentEmployeeCommandHandler
    : IRequestHandler<UpdateDepartmentEmployeeCommand, Result<DepartmentMemberDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateDepartmentEmployeeCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DepartmentMemberDto>> Handle(
        UpdateDepartmentEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<DepartmentMemberDto>.Failure($"Phòng ban với ID '{request.DepartmentId}' không tồn tại.");
        }

        // 1. Phân quyền người gọi
        var callerRole = _currentUserService.Role;
        var callerDeptId = _currentUserService.DepartmentId;
        var callerTenantId = _currentUserService.TenantId;

        if (callerRole == "DepartmentAdmin")
        {
            if (!callerDeptId.HasValue && _currentUserService.UserId.HasValue)
            {
                var caller = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);
                callerDeptId = caller?.DepartmentId;
            }

            if (!callerDeptId.HasValue || callerDeptId.Value != request.DepartmentId)
            {
                return Result<DepartmentMemberDto>.Failure("Bạn chỉ có quyền cập nhật nhân viên trong phòng ban của mình.");
            }
        }
        else if (callerRole == "TenantAdmin")
        {
            if (!callerTenantId.HasValue && _currentUserService.UserId.HasValue)
            {
                var caller = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);
                callerTenantId = caller?.TenantId;
            }

            if (!callerTenantId.HasValue || callerTenantId.Value != department.TenantId)
            {
                return Result<DepartmentMemberDto>.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }
        }
        else if (callerRole != "SystemAdmin")
        {
            return Result<DepartmentMemberDto>.Failure("Bạn không có quyền chỉnh sửa nhân viên của phòng ban này.");
        }

        // 2. Tìm nhân viên mục tiêu
        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null || user.DepartmentId != request.DepartmentId)
        {
            return Result<DepartmentMemberDto>.Failure($"Không tìm thấy nhân viên với ID '{request.UserId}' trong phòng ban này.");
        }

        // 3. Quy tắc an toàn: Không cho phép chỉnh sửa vai trò cấp cao hơn hoặc DepartmentAdmin thông qua chức năng quản lý nhân viên
        var targetRoles = user.UserRoles.Select(ur => ur.Role?.Name).Where(n => n != null).ToList();
        if (targetRoles.Contains("SystemAdmin") || targetRoles.Contains("TenantAdmin"))
        {
            return Result<DepartmentMemberDto>.Failure("Không thể chỉnh sửa quản trị viên cấp cao từ giao diện quản lý phòng ban.");
        }

        if (targetRoles.Contains("DepartmentAdmin") && callerRole == "DepartmentAdmin" && _currentUserService.UserId != user.Id)
        {
            return Result<DepartmentMemberDto>.Failure("Không thể thay đổi thông tin của Trưởng phòng khác.");
        }

        // 4. Cập nhật thông tin cơ bản
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        var wasActive = user.IsActive;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        // Nếu chuyển sang vô hiệu hóa -> Thu hồi tất cả Refresh Token của user
        if (wasActive && !request.IsActive)
        {
            var refreshTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == user.Id)
                .ToListAsync(cancellationToken);

            if (refreshTokens.Any())
            {
                _context.RefreshTokens.RemoveRange(refreshTokens);
            }
        }

        // 5. Cập nhật hạn mức lưu trữ (nếu có yêu cầu)
        var userQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == user.Id, cancellationToken);

        if (request.StorageQuotaBytes.HasValue)
        {
            var newQuota = request.StorageQuotaBytes.Value;
            if (newQuota < 0)
            {
                return Result<DepartmentMemberDto>.Failure("Hạn mức lưu trữ không được là số âm.");
            }

            if (userQuota != null && newQuota < userQuota.UsedBytes)
            {
                return Result<DepartmentMemberDto>.Failure(
                    $"Không thể hạ hạn mức cá nhân thấp hơn dung lượng người dùng đã sử dụng ({FormatBytes(userQuota.UsedBytes)}).");
            }

            var otherUsersQuotaSum = await _context.UserStorageQuotas
                .Where(q => q.User.DepartmentId == department.Id && q.UserId != user.Id)
                .SumAsync(q => q.QuotaBytes, cancellationToken);

            var availableInDept = Math.Max(0, department.StorageQuotaBytes - otherUsersQuotaSum);
            if (newQuota > availableInDept)
            {
                return Result<DepartmentMemberDto>.Failure(
                    $"Hạn mức yêu cầu ({FormatBytes(newQuota)}) vượt quá dung lượng khả dụng của phòng ban (Phòng còn trống: {FormatBytes(availableInDept)}).");
            }

            if (userQuota == null)
            {
                userQuota = new UserStorageQuota
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    QuotaBytes = newQuota,
                    UsedBytes = 0
                };
                await _context.UserStorageQuotas.AddAsync(userQuota, cancellationToken);
            }
            else
            {
                userQuota.QuotaBytes = newQuota;
            }
        }

        // 6. Ghi AuditLog
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = department.TenantId,
            UserId = _currentUserService.UserId,
            Action = "UpdateDepartmentEmployee",
            EntityName = "User",
            EntityId = user.Id,
            Details = $"Cập nhật thông tin nhân viên '{user.FullName}' trong phòng '{department.Name}'. Trạng thái: {(user.IsActive ? "Kích hoạt" : "Vô hiệu hóa")}. Hạn mức: {(userQuota != null ? FormatBytes(userQuota.QuotaBytes) : "Chưa cấp")}.",
            CreatedAt = DateTime.UtcNow
        };
        _context.AuditLogs.Add(auditLog);

        await _context.SaveChangesAsync(cancellationToken);

        return Result<DepartmentMemberDto>.Success(DepartmentMemberDto.Create(user, userQuota));
    }
}
