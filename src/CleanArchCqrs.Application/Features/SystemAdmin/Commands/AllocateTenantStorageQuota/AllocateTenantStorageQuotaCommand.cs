using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.SystemAdmin.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.AllocateTenantStorageQuota;

public record AllocateTenantStorageQuotaCommand(
    Guid TenantId,
    Guid StorageProviderId,
    long QuotaBytes
) : IRequest<TenantStorageQuotaDto>;

public class AllocateTenantStorageQuotaCommandHandler : IRequestHandler<AllocateTenantStorageQuotaCommand, TenantStorageQuotaDto>
{
    private readonly IApplicationDbContext _context;

    public AllocateTenantStorageQuotaCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TenantStorageQuotaDto> Handle(AllocateTenantStorageQuotaCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .Include(t => t.StorageProvider)
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);

        if (tenant == null)
            throw new KeyNotFoundException($"Không tìm thấy Tenant với ID {request.TenantId}");

        var provider = await _context.StorageProviders
            .Include(sp => sp.Tenants)
            .FirstOrDefaultAsync(sp => sp.Id == request.StorageProviderId, cancellationToken);

        if (provider == null)
            throw new KeyNotFoundException($"Không tìm thấy Cloud Provider với ID {request.StorageProviderId}");

        if (!provider.IsActive)
            throw new InvalidOperationException($"Cloud Provider '{provider.Name}' hiện đang bị vô hiệu hóa.");

        // Kiểm tra dung lượng khả dụng của provider (trừ các tenant khác đang gán vào provider này)
        var totalCapacity = provider.TotalCapacityBytes > 0 ? provider.TotalCapacityBytes : (15L * 1024 * 1024 * 1024);
        var cloudFree = Math.Max(0, totalCapacity - provider.UsedCapacityBytes);
        var allocatedToOthers = provider.Tenants
            .Where(t => t.Id != tenant.Id)
            .Sum(t => t.StorageQuotaBytes);

        var availableForThisTenant = Math.Max(0, cloudFree - allocatedToOthers);

        if (request.QuotaBytes > availableForThisTenant)
        {
            var availableGB = Math.Round((double)availableForThisTenant / (1024 * 1024 * 1024), 2);
            var requestedGB = Math.Round((double)request.QuotaBytes / (1024 * 1024 * 1024), 2);
            throw new InvalidOperationException($"Vượt quá dung lượng khả dụng của Cloud Provider! Bạn yêu cầu {requestedGB} GB nhưng Cloud chỉ còn {availableGB} GB.");
        }

        // Kiểm tra không được hạ quota thấp hơn dung lượng tenant đã sử dụng
        if (request.QuotaBytes < tenant.StorageUsedBytes)
        {
            var usedGB = Math.Round((double)tenant.StorageUsedBytes / (1024 * 1024 * 1024), 2);
            throw new InvalidOperationException($"Không thể cấp quota nhỏ hơn dung lượng mà Tenant đã sử dụng ({usedGB} GB).");
        }

        // Cập nhật
        tenant.StorageProviderId = provider.Id;
        tenant.StorageQuotaBytes = request.QuotaBytes;

        await _context.SaveChangesAsync(cancellationToken);

        return new TenantStorageQuotaDto
        {
            TenantId = tenant.Id,
            TenantName = tenant.Name,
            TenantCode = tenant.Code,
            StorageProviderId = provider.Id,
            StorageProviderName = provider.Name,
            StorageProviderType = provider.Type,
            StorageQuotaBytes = tenant.StorageQuotaBytes,
            StorageUsedBytes = tenant.StorageUsedBytes
        };
    }
}
