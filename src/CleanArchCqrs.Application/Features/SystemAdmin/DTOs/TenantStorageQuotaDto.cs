using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Features.SystemAdmin.DTOs;

public class TenantStorageQuotaDto
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;

    public Guid? StorageProviderId { get; set; }
    public string? StorageProviderName { get; set; }
    public StorageProviderType? StorageProviderType { get; set; }

    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public long AllocatedToDepartmentsBytes { get; set; }
    public long UnallocatedBytes => Math.Max(0, StorageQuotaBytes - AllocatedToDepartmentsBytes);
    public long AvailableBytes => Math.Max(0, StorageQuotaBytes - Math.Max(StorageUsedBytes, AllocatedToDepartmentsBytes));
    public double UsagePercentage => StorageQuotaBytes > 0 
        ? Math.Round((double)Math.Max(StorageUsedBytes, AllocatedToDepartmentsBytes) / StorageQuotaBytes * 100, 2) 
        : 0;
}
