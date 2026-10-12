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
    private long _quotaBytes;
    private long _usedBytes;

    public long QuotaBytes
    {
        get => _quotaBytes;
        set => _quotaBytes = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Quota cannot be negative.");
    }

    public long UsedBytes
    {
        get => _usedBytes;
        set => _usedBytes = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Used bytes cannot be negative.");
    }

    // Compatibility aliases used by the department-admin endpoints retained from main.
    public long StorageQuotaBytes { get => QuotaBytes; set => QuotaBytes = value; }
    public long StorageUsedBytes { get => UsedBytes; set => UsedBytes = value; }

    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<UserStorageQuota> UserStorageQuotas { get; set; } = new List<UserStorageQuota>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();

    public Department() { }

    public Department(Guid id, Guid tenantId, string name, string code) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Code = code;
        IsActive = true;
    }
}

