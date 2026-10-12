using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentUsersStorageQuota;

public record GetDepartmentUsersStorageQuotaQuery(Guid DepartmentId) : IRequest<Result<DepartmentUsersQuotaSummaryDto>>;

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
        // Kiểm tra quyền Caller: Nếu là DepartmentAdmin/Employee thì chỉ được xem phòng ban của chính mình
        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        var isTenantAdmin = _currentUserService.Role == "TenantAdmin";
        if (!isSystemAdmin && !isTenantAdmin)
        {
            if (_currentUserService.DepartmentId.HasValue && _currentUserService.DepartmentId.Value != request.DepartmentId)
            {
                return Result<DepartmentUsersQuotaSummaryDto>.Failure("Bạn không có quyền xem thông tin hạn mức của phòng ban này.");
            }
        }

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, cancellationToken);

        if (department == null)
        {
            return Result<DepartmentUsersQuotaSummaryDto>.Failure("Không tìm thấy thông tin Phòng ban.");
        }

        if (isTenantAdmin && _currentUserService.TenantId.HasValue && department.TenantId != _currentUserService.TenantId.Value)
        {
            return Result<DepartmentUsersQuotaSummaryDto>.Failure("Phòng ban không thuộc tổ chức của bạn.");
        }

        var users = await _context.Users
            .Where(u => u.DepartmentId == department.Id && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToList();

        var quotas = await _context.UserStorageQuotas
            .Where(q => userIds.Contains(q.UserId))
            .ToDictionaryAsync(q => q.UserId, cancellationToken);

        var userDtos = users.Select(u =>
        {
            quotas.TryGetValue(u.Id, out var q);
            return new UserStorageQuotaDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                DepartmentId = department.Id,
                DepartmentName = department.Name,
                QuotaBytes = q?.QuotaBytes ?? 0,
                UsedBytes = q?.UsedBytes ?? 0
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
