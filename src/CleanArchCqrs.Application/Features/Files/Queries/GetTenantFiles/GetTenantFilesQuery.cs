using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.DTOs;
using MediatR;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetTenantFiles;

public record GetTenantFilesQuery : IRequest<Result<TenantStorageSummaryDto>>
{
    public Guid? TenantId { get; init; }
    public Guid? FolderId { get; init; }
}
