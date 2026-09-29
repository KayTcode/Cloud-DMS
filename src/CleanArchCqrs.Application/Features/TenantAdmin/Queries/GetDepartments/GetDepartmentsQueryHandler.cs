using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.TenantAdmin.Queries.GetDepartments;

public class GetDepartmentsQueryHandler : IRequestHandler<GetDepartmentsQuery, Result<List<DepartmentDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetDepartmentsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<DepartmentDto>>> Handle(
        GetDepartmentsQuery request,
        CancellationToken cancellationToken)
    {
        var departments = await _context.Departments
            .AsNoTracking()
            .Where(d => d.TenantId == request.TenantId)
            .Include(d => d.Users)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var dtos = departments.Select(DepartmentDto.FromEntity).ToList();

        return Result<List<DepartmentDto>>.Success(dtos);
    }
}
