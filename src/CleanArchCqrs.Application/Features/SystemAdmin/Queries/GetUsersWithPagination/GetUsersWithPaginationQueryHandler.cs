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

    public GetUsersWithPaginationQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PaginatedList<UserDto>>> Handle(
        GetUsersWithPaginationQuery request, 
        CancellationToken cancellationToken)
    {
        var query = _context.Users
            .AsNoTracking()
            .Include(u => u.Tenant)
            .Include(u => u.Department)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .AsQueryable();

        // 1. Search term filter (Email, FirstName, LastName, PhoneNumber)
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var search = request.SearchTerm.Trim().ToLower();
            query = query.Where(u => 
                u.FirstName.ToLower().Contains(search) || 
                u.LastName.ToLower().Contains(search) || 
                u.Email.ToLower().Contains(search) || 
                (u.PhoneNumber != null && u.PhoneNumber.Contains(search)));
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

        // 7. Paginate
        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 10 : request.PageSize;

        var users = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtoList = users.Select(UserDto.FromEntity).ToList();

        var paginatedResult = new PaginatedList<UserDto>(dtoList, totalCount, pageNumber, pageSize);
        return Result<PaginatedList<UserDto>>.Success(paginatedResult);
    }
}
