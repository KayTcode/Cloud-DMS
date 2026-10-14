using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.CreateEmployee;

public record CreateEmployeeCommand : IRequest<Result<UserDto>>
{
    public Guid DepartmentId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? Password { get; init; }
    public long? StorageQuotaBytes { get; init; }
}
