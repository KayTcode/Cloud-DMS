using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FileContentVersion : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public long DocumentVersion { get; set; } = 1;
    public string Hash { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public Guid ProviderId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long RetainedStagingBytes { get; set; }
    public DateTimeOffset? PhysicalStorageDeletedAt { get; set; }
    public FileScanStatus ScanStatus { get; set; } = FileScanStatus.Pending;
}
