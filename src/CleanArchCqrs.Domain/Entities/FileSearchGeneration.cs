using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FileSearchGeneration : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public long DocumentVersion { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string ExtractorVersion { get; set; } = string.Empty;
    public string ChunkerVersion { get; set; } = string.Empty;
    public FileSearchStatus Status { get; set; } = FileSearchStatus.Pending;
    public bool IsPartial { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
