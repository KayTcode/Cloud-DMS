using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FileUploadSession : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public Guid UserId { get; set; }
    public Guid QuotaUserId { get; set; }
    public long DocumentVersion { get; set; }
    public Guid ProviderId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string MultipartUploadId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public Guid? FolderId { get; set; }
    public Guid? DepartmentId { get; set; }
    public long? PreviousDocumentVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? CompletionManifestHash { get; set; }
    public FileUploadStatus Status { get; set; } = FileUploadStatus.Pending;
    public long ReservedBytes { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? StorageCleanedAt { get; set; }
}
