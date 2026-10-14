using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentUsersStorageQuota;

public record GetDepartmentUsersStorageQuotaQuery(Guid DepartmentId) : IRequest<Result<DepartmentUsersQuotaSummaryDto>>;
