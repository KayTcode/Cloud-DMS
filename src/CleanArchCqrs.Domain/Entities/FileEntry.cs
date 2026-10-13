using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FileEntry : BaseEntity
{
    public long DocumentVersion { get; set; } = 1;
    public Guid? ActiveSearchGenerationId { get; set; }

    public Guid TenantId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? OwningDepartmentId { get; set; }

    public Guid? FolderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? Hash { get; set; }

    public string ScanStatus { get; set; } = "Clean";

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public User? DeletedByUser { get; set; }

    public FileLifecycle Lifecycle { get; set; } = FileLifecycle.Active;

    public long AuthorizationVersion { get; set; } = 1;

    public DateTimeOffset? QuotaReleasedAt { get; set; }

    public Guid? StorageProviderId { get; set; }
    public StorageProvider? StorageProvider { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public User Owner { get; set; } = null!;

    public Department? OwningDepartment { get; set; }

    public Folder? Folder { get; set; }

    public ICollection<FileShare> Shares { get; set; } = new List<FileShare>();
}
