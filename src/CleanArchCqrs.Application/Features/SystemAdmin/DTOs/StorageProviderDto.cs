using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Features.SystemAdmin.DTOs;

public class StorageProviderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public StorageProviderType Type { get; set; }
    public string TypeName => Type switch
    {
        StorageProviderType.GoogleDrive => "Google Drive",
        StorageProviderType.OneDrive => "OneDrive",
        StorageProviderType.Local => "Local Storage",
        _ => "Unknown"
    };

    public long TotalCapacityBytes { get; set; }
    public long UsedCapacityBytes { get; set; }
    public long AvailableCapacityBytes => Math.Max(0, TotalCapacityBytes - UsedCapacityBytes);

    public long AllocatedToTenantsBytes { get; set; }
    public long UnallocatedCapacityBytes => Math.Max(0, AvailableCapacityBytes - AllocatedToTenantsBytes);

    public string? RootFolderId { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = "Connected";
    public DateTime? LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TenantsCount { get; set; }
}

public class StorageProviderAvailableQuotaDto
{
    public Guid ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public StorageProviderType Type { get; set; }
    public long TotalCapacityBytes { get; set; }
    public long CloudUsedBytes { get; set; }
    public long CloudFreeBytes { get; set; }
    public long AlreadyAllocatedBytes { get; set; }
    public long AvailableToAllocateBytes { get; set; }
}
