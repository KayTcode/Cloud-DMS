using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentSetupInfo;

public record GetDepartmentSetupInfoQuery : IRequest<Result<DepartmentSetupInfoDto>>;
