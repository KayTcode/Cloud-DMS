using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FilePreviewArtifact : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public long DocumentVersion { get; set; }
    public string ConverterVersion { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public Guid ProviderId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public FilePreviewStatus Status { get; set; } = FilePreviewStatus.Pending;
    public DateTimeOffset? ExpiresAt { get; set; }
}
