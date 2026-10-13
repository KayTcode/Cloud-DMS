using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class LegacyUploadIntent : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid FileId { get; set; }
    public string? ParentFolderId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? StorageCleanedAt { get; set; }
}
