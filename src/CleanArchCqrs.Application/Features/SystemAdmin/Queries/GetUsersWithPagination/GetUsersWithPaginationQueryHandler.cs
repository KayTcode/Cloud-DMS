using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetUsersWithPagination;

public class GetUsersWithPaginationQueryHandler 
    : IRequestHandler<GetUsersWithPaginationQuery, Result<PaginatedList<UserDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetUsersWithPaginationQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PaginatedList<UserDto>>> Handle(
        GetUsersWithPaginationQuery request, 
        CancellationToken cancellationToken)
    {
        var query = _context.Users
            .AsNoTracking()
            .AsSplitQuery()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .AsQueryable();

        // Phân quyền dữ liệu theo phạm vi người gọi
        var callerRole = _currentUserService.Role;
        if (callerRole == "SystemAdmin")
        {
            // System Admin có toàn quyền xem tất cả người dùng trong hệ thống.
            // Chỉ lọc khi client chủ động truyền RoleId, TenantId hoặc SearchTerm.
        }
        else if (callerRole == "TenantAdmin")
        {
            // Tenant Admin chỉ được xem các người dùng trong tổ chức của mình
            var tid = _currentUserService.TenantId;
            if (!tid.HasValue && _currentUserService.UserId.HasValue)
            {
                var callerUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);
                tid = callerUser?.TenantId;
            }

            if (tid.HasValue)
            {
                query = query.Where(u => u.TenantId == tid.Value);
            }
            else
            {
                return Result<PaginatedList<UserDto>>.Failure("Không tìm thấy thông tin tổ chức của người dùng.");
            }
        }
        else if (callerRole == "DepartmentAdmin")
        {
            // Department Admin chỉ được xem các nhân viên trong phòng ban của mình
            var did = _currentUserService.DepartmentId;
            if (!did.HasValue && _currentUserService.UserId.HasValue)
            {
                var callerUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value, cancellationToken);
                did = callerUser?.DepartmentId;
            }

            if (did.HasValue)
            {
                query = query.Where(u => u.DepartmentId == did.Value);
            }
            else
            {
                // Nếu Trưởng phòng chưa thiết lập phòng ban, chỉ hiển thị chính mình
                query = query.Where(u => u.Id == (_currentUserService.UserId ?? Guid.Empty));
            }
        }
        else
        {
            // Người dùng thông thường chỉ xem được chính mình
            query = query.Where(u => u.Id == (_currentUserService.UserId ?? Guid.Empty));
        }

        // 1. Search term filter (Email, FirstName, LastName, PhoneNumber) - Sargable search
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            query = query.Where(u => 
                EF.Functions.Like(u.FirstName, pattern) || 
                EF.Functions.Like(u.LastName, pattern) || 
                EF.Functions.Like(u.Email, pattern) || 
                (u.PhoneNumber != null && EF.Functions.Like(u.PhoneNumber, pattern)));
        }

        // 2. Role filter (by RoleId)
        if (request.RoleId.HasValue)
        {
            query = query.Where(u => u.UserRoles.Any(ur => ur.RoleId == request.RoleId.Value));
        }

        // 3. Tenant filter
        if (request.TenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == request.TenantId.Value);
        }

        // 4. Department filter
        if (request.DepartmentId.HasValue)
        {
            query = query.Where(u => u.DepartmentId == request.DepartmentId.Value);
        }

        // 5. Status filter
        if (request.IsActive.HasValue)
        {
            query = query.Where(u => u.IsActive == request.IsActive.Value);
        }

        // 6. Order by CreatedAt descending
        query = query.OrderByDescending(u => u.CreatedAt);

        // 7. Paginate (Cap pageSize <= 100 theo BUG-27)
        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 10 : request.PageSize, 1, 100);

        var users = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtoList = users.Select(UserDto.FromEntity).ToList();

        var paginatedResult = new PaginatedList<UserDto>(dtoList, totalCount, pageNumber, pageSize);
        return Result<PaginatedList<UserDto>>.Success(paginatedResult);
    }
}
