using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.TenantAdmin.Commands.CreateDepartmentAdmin;

public record CreateDepartmentAdminCommand : IRequest<Result<UserDto>>
{
    public Guid TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
}
