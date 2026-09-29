using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetUsersWithPagination;

public record GetUsersWithPaginationQuery : IRequest<Result<PaginatedList<UserDto>>>
{
    public string? SearchTerm { get; init; }
    public Guid? RoleId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public bool? IsActive { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}
