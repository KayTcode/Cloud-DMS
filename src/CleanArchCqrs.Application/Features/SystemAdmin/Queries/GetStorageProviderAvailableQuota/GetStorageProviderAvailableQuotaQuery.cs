using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetStorageProviderAvailableQuota;

public record GetStorageProviderAvailableQuotaQuery(Guid ProviderId) : IRequest<StorageProviderAvailableQuotaDto?>;

public class GetStorageProviderAvailableQuotaQueryHandler : IRequestHandler<GetStorageProviderAvailableQuotaQuery, StorageProviderAvailableQuotaDto?>
{
    private readonly IApplicationDbContext _context;

    public GetStorageProviderAvailableQuotaQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StorageProviderAvailableQuotaDto?> Handle(GetStorageProviderAvailableQuotaQuery request, CancellationToken cancellationToken)
    {
        var provider = await _context.StorageProviders
            .Include(sp => sp.Tenants)
            .FirstOrDefaultAsync(sp => sp.Id == request.ProviderId, cancellationToken);

        if (provider == null)
            return null;

        var totalCapacity = provider.TotalCapacityBytes > 0 ? provider.TotalCapacityBytes : (15L * 1024 * 1024 * 1024);
        var cloudFree = Math.Max(0, totalCapacity - provider.UsedCapacityBytes);
        var alreadyAllocated = provider.Tenants.Sum(t => t.StorageQuotaBytes);
        var availableToAllocate = Math.Max(0, cloudFree - alreadyAllocated);

        return new StorageProviderAvailableQuotaDto
        {
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            Type = provider.Type,
            TotalCapacityBytes = totalCapacity,
            CloudUsedBytes = provider.UsedCapacityBytes,
            CloudFreeBytes = cloudFree,
            AlreadyAllocatedBytes = alreadyAllocated,
            AvailableToAllocateBytes = availableToAllocate
        };
    }
}
