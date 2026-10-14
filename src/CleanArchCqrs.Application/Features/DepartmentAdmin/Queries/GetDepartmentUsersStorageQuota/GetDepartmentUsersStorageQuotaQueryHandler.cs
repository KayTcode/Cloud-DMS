using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentUsersStorageQuota;

public class GetDepartmentUsersStorageQuotaQueryHandler : IRequestHandler<GetDepartmentUsersStorageQuotaQuery, Result<DepartmentUsersQuotaSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetDepartmentUsersStorageQuotaQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DepartmentUsersQuotaSummaryDto>> Handle(
        GetDepartmentUsersStorageQuotaQuery request,
        CancellationToken cancellationToken)
    {
        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<DepartmentUsersQuotaSummaryDto>.Failure("Không tìm thấy thông tin Phòng ban.");
        }

        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        var isTenantAdmin = _currentUserService.Role == "TenantAdmin";

        if (!isSystemAdmin)
        {
            var callerTenantId = _currentUserService.TenantId;
            if (!callerTenantId.HasValue && _currentUserService.UserId.HasValue)
            {
                callerTenantId = await _context.Users
                    .Where(u => u.Id == _currentUserService.UserId.Value)
                    .Select(u => u.TenantId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (!callerTenantId.HasValue || department.TenantId != callerTenantId.Value)
            {
                return Result<DepartmentUsersQuotaSummaryDto>.Failure("Phòng ban không thuộc tổ chức của bạn.");
            }

            if (!isTenantAdmin)
            {
                var callerDeptId = _currentUserService.DepartmentId;
                if (!callerDeptId.HasValue && _currentUserService.UserId.HasValue)
                {
                    callerDeptId = await _context.Users
                        .Where(u => u.Id == _currentUserService.UserId.Value)
                        .Select(u => u.DepartmentId)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (!callerDeptId.HasValue || callerDeptId.Value != department.Id)
                {
                    return Result<DepartmentUsersQuotaSummaryDto>.Failure("Bạn không có quyền xem thông tin hạn mức của phòng ban này.");
                }
            }
        }

        var users = await _context.Users
            .Where(u => u.DepartmentId == department.Id && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToList();

        var quotas = await _context.UserStorageQuotas
            .Where(q => userIds.Contains(q.UserId))
            .ToDictionaryAsync(q => q.UserId, cancellationToken);

        // Đếm số lượng tệp tin hoạt động (chưa bị xóa) của từng nhân viên
        var fileCounts = await _context.FileEntries
            .Where(f => userIds.Contains(f.OwnerId) && !f.IsDeleted)
            .GroupBy(f => f.OwnerId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        var userDtos = users.Select(u =>
        {
            quotas.TryGetValue(u.Id, out var q);
            fileCounts.TryGetValue(u.Id, out var fileCount);

            return new UserStorageQuotaDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                DepartmentId = department.Id,
                DepartmentName = department.Name,
                QuotaBytes = q?.QuotaBytes ?? 0,
                UsedBytes = q?.UsedBytes ?? 0,
                FileCount = fileCount
            };
        }).ToList();


        long totalAllocated = userDtos.Sum(x => x.QuotaBytes);

        var summary = new DepartmentUsersQuotaSummaryDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            DepartmentQuotaBytes = department.StorageQuotaBytes,
            DepartmentUsedBytes = department.StorageUsedBytes,
            TotalAllocatedToUsersBytes = totalAllocated,
            Users = userDtos
        };

        return Result<DepartmentUsersQuotaSummaryDto>.Success(summary);
    }
}
