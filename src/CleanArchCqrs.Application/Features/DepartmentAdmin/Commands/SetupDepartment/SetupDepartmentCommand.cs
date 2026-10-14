using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.SetupDepartment;

public record SetupDepartmentCommand : IRequest<Result<DepartmentDto>>
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public double? AdminPersonalQuotaGB { get; init; }
    public long? AdminPersonalQuotaBytes { get; init; }
}
