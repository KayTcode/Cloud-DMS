using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class FileEntry : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? FolderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? Hash { get; set; }

    public bool IsDeleted { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public User Owner { get; set; } = null!;

    public Folder? Folder { get; set; }

    public ICollection<FileShare> Shares { get; set; } = new List<FileShare>();
}
