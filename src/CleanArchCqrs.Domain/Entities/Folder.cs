using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class Folder : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid? ParentFolderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;

    public Folder? ParentFolder { get; set; }

    public ICollection<Folder> Children { get; set; } = new List<Folder>();

    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();
}
