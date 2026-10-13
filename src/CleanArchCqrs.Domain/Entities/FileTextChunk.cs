using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class FileTextChunk : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid GenerationId { get; set; }
    public int Ordinal { get; set; }
    public string Text { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string? Location { get; set; }
}
