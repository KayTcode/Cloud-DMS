using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.CreateUser;

public record CreateUserCommand : IRequest<Result<UserDto>>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public List<Guid> RoleIds { get; init; } = new();
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
}
