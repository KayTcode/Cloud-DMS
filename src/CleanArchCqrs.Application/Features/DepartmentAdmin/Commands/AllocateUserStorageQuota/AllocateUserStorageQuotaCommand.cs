using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.AllocateUserStorageQuota;

public record AllocateUserStorageQuotaCommand(
    Guid DepartmentId,
    Guid UserId,
    long QuotaBytes
) : IRequest<Result<UserStorageQuotaDto>>;
