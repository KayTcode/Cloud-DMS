using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Queries.GetTenantStorageQuota;

public record GetTenantStorageQuotaQuery(Guid TenantId) : IRequest<TenantStorageQuotaDto?>;

public class GetTenantStorageQuotaQueryHandler : IRequestHandler<GetTenantStorageQuotaQuery, TenantStorageQuotaDto?>
{
    private readonly IApplicationDbContext _context;

    public GetTenantStorageQuotaQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TenantStorageQuotaDto?> Handle(GetTenantStorageQuotaQuery request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .Include(t => t.StorageProvider)
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);

        if (tenant == null)
            return null;

        return new TenantStorageQuotaDto
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            TenantCode = tenant.Code,
            StorageProviderId = tenant.StorageProviderId,
            StorageProviderName = tenant.StorageProvider?.Name,
            StorageProviderType = tenant.StorageProvider?.Type,
            StorageQuotaBytes = tenant.StorageQuotaBytes,
            StorageUsedBytes = tenant.StorageUsedBytes
        };
    }
}
