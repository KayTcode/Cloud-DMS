using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentSetupInfo;

public class GetDepartmentSetupInfoQueryHandler : IRequestHandler<GetDepartmentSetupInfoQuery, Result<DepartmentSetupInfoDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetDepartmentSetupInfoQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DepartmentSetupInfoDto>> Handle(
        GetDepartmentSetupInfoQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        var tenantId = _currentUserService.TenantId;

        if (!userId.HasValue || !tenantId.HasValue)
        {
            return Result<DepartmentSetupInfoDto>.Failure("Yêu cầu không hợp lệ: Chưa xác thực danh tính người dùng hoặc tổ chức.");
        }

        var user = await _context.Users
            .Include(u => u.Tenant)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);

        if (user == null)
        {
            return Result<DepartmentSetupInfoDto>.Failure("Không tìm thấy thông tin người dùng.");
        }

        var roles = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role.Name)
            .ToList();

        if (!roles.Contains("DepartmentAdmin"))
        {
            return Result<DepartmentSetupInfoDto>.Failure("Chỉ tài khoản Trưởng phòng (Department Admin) mới có quyền truy cập thông tin này.");
        }

        // Trường hợp 1: Tài khoản đã hoàn tất thiết lập phòng ban trước đó
        if (user.DepartmentId.HasValue)
        {
            var dept = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == user.DepartmentId.Value, cancellationToken);

            long deptQuotaBytes = dept?.StorageQuotaBytes ?? 0;
            double deptQuotaGB = Math.Round((double)deptQuotaBytes / (1024 * 1024 * 1024), 2);

            var alreadySetupDto = new DepartmentSetupInfoDto
            {
                AssignedQuotaBytes = deptQuotaBytes,
                AssignedQuotaGB = deptQuotaGB,
                AdminName = user.FullName,
                TenantName = user.Tenant?.Name ?? "Tổ chức",
                HasDepartment = true,
                DepartmentId = user.DepartmentId
            };

            return Result<DepartmentSetupInfoDto>.Success(alreadySetupDto);
        }

        // Trường hợp 2: Tài khoản chưa thiết lập phòng ban, lấy Quota ví tạm do TenantAdmin phân bổ
        var adminQuota = await _context.UserStorageQuotas
            .FirstOrDefaultAsync(q => q.UserId == user.Id, cancellationToken);

        if (adminQuota == null || adminQuota.QuotaBytes <= 0)
        {
            return Result<DepartmentSetupInfoDto>.Failure(
                "Tài khoản của bạn chưa được cấp hạn mức lưu trữ (Quota). Vui lòng liên hệ Quản trị viên.");
        }

        long assignedQuotaBytes = adminQuota.QuotaBytes;
        double assignedQuotaGB = Math.Round((double)assignedQuotaBytes / (1024 * 1024 * 1024), 2);

        var dto = new DepartmentSetupInfoDto
        {
            AssignedQuotaBytes = assignedQuotaBytes,
            AssignedQuotaGB = assignedQuotaGB,
            AdminName = user.FullName,
            TenantName = user.Tenant?.Name ?? "Tổ chức",
            HasDepartment = false,
            DepartmentId = null
        };

        return Result<DepartmentSetupInfoDto>.Success(dto);
    }
}
