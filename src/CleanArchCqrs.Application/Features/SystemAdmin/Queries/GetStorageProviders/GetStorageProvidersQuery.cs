using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetStorageProviders;

public record GetStorageProvidersQuery : IRequest<List<StorageProviderDto>>;

public class GetStorageProvidersQueryHandler : IRequestHandler<GetStorageProvidersQuery, List<StorageProviderDto>>
{
    private readonly IApplicationDbContext _context;

    public GetStorageProvidersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<StorageProviderDto>> Handle(GetStorageProvidersQuery request, CancellationToken cancellationToken)
    {
        var providers = await _context.StorageProviders
            .Include(sp => sp.Tenants)
            .OrderByDescending(sp => sp.CreatedAt)
            .ToListAsync(cancellationToken);

        return providers.Select(p => 
        {
            var total = p.TotalCapacityBytes > 0 ? p.TotalCapacityBytes : (15L * 1024 * 1024 * 1024);
            return new StorageProviderDto
            {
                Id = p.Id,
                Name = p.Name,
                Type = p.Type,
                TotalCapacityBytes = total,
                UsedCapacityBytes = p.UsedCapacityBytes,
                AllocatedToTenantsBytes = p.Tenants.Sum(t => t.StorageQuotaBytes),
                RootFolderId = p.RootFolderId,
                IsActive = p.IsActive,
                Status = p.Status,
                LastSyncAt = p.LastSyncAt,
                CreatedAt = p.CreatedAt,
                TenantsCount = p.Tenants.Count
            };
        }).ToList();
    }
}
