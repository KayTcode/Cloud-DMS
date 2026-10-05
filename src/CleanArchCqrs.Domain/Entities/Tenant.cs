using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

/// <summary>
/// Represents an enterprise tenant (organization / company).
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Unique tenant identifier (e.g., FPT, VNG)
    public string? Description { get; set; }
    public long StorageQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024; // Default 10 GB
    public long StorageUsedBytes { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? StorageProviderId { get; set; }
    public StorageProvider? StorageProvider { get; set; }

    // Navigation properties
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();

    public Tenant() { }

    public Tenant(Guid id, string name, string code, long storageQuotaBytes) : base(id)
    {
        Name = name;
        Code = code;
        StorageQuotaBytes = storageQuotaBytes;
        IsActive = true;
    }
}
