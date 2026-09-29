using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.TenantAdmin.Queries.GetDepartments;

public record GetDepartmentsQuery(Guid TenantId) : IRequest<Result<List<DepartmentDto>>>;
