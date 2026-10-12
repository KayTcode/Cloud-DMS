using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

/// <summary>
/// Represents a department inside an enterprise tenant (e.g., IT, HR, Accounting).
/// </summary>
public class Department : BaseEntity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Unique code within tenant (e.g., IT, HR)
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public long StorageQuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024; // Default 5 GB
    public long StorageUsedBytes { get; set; }

    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();

    public Department() { }

    public Department(Guid id, Guid tenantId, string name, string code, long storageQuotaBytes = 5L * 1024 * 1024 * 1024) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Code = code;
        StorageQuotaBytes = storageQuotaBytes;
        IsActive = true;
    }
}
