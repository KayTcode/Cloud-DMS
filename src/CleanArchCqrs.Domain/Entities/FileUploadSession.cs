using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FileUploadSession : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public Guid UserId { get; set; }
    public long DocumentVersion { get; set; }
    public Guid ProviderId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public FileUploadStatus Status { get; set; } = FileUploadStatus.Pending;
    public long ReservedBytes { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
