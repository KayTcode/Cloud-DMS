using CleanArchCqrs.Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Represents an enterprise tenant (organization / company).
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Unique tenant identifier (e.g., FPT, VNG)
    public string? Description { get; set; }
    public long StorageQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024; // Default 10 GB
    public long StorageUsedBytes { get; private set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<FileEntry> Files { get; private set; }
        = new List<FileEntry>();
}
