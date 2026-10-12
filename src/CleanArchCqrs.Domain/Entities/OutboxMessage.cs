using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class OutboxMessage : BaseEntity
{
    public string AggregateKey { get; set; } = string.Empty;
    public long Version { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
}
