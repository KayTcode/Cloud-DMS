using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class FileShare : BaseEntity
{
    public Guid FileEntryId { get; set; }

    public Guid SharedWithUserId { get; set; }

    public bool CanRead { get; set; }

    public bool CanWrite { get; set; }

    public bool CanDelete { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public FileEntry FileEntry { get; set; } = null!;

    public User SharedWithUser { get; set; } = null!;
}
