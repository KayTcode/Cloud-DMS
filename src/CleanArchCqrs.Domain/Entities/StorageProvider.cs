using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public enum StorageProviderType
{
    Local = 0,
    GoogleDrive = 1,
    OneDrive = 2
}

public class StorageProvider : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public StorageProviderType Type { get; set; }
    public string ConfigEncrypted { get; set; } = string.Empty;
    public long TotalCapacityBytes { get; set; }
    public long UsedCapacityBytes { get; set; }
    public string? RootFolderId { get; set; }
    public bool IsActive { get; set; } = true;
    public string Status { get; set; } = "Connected";
    public DateTime? LastSyncAt { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();

    public StorageProvider() { }

    public StorageProvider(string name, StorageProviderType type, string configEncrypted, long totalCapacityBytes, long usedCapacityBytes, string? rootFolderId)
    {
        Id = Guid.NewGuid();
        Name = name;
        Type = type;
        ConfigEncrypted = configEncrypted;
        TotalCapacityBytes = totalCapacityBytes;
        UsedCapacityBytes = usedCapacityBytes;
        RootFolderId = rootFolderId;
        IsActive = true;
        Status = "Connected";
        LastSyncAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }
}
