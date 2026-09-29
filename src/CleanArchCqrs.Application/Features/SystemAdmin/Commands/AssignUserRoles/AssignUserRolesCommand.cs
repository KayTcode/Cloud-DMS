using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.AssignUserRoles;

public record AssignUserRolesCommand : IRequest<Result<UserDto>>
{
    public Guid Id { get; init; }
    public List<Guid> RoleIds { get; init; } = new();
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
}
