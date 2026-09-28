using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class Folder : BaseEntity
{
    public Guid TenantId { get; private set; }

    public Guid? ParentFolderId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Tenant Tenant { get; private set; } = null!;

    public Folder? ParentFolder { get; private set; }

    public ICollection<Folder> Children { get; private set; }
        = new List<Folder>();

    public ICollection<FileEntry> Files { get; private set; }
        = new List<FileEntry>();
}
