using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class AuthorizationOperation : BaseEntity
{
    public Guid? InitiatedByUserId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public long Version { get; set; }
    public AuthorizationOperationStatus Status { get; set; } = AuthorizationOperationStatus.Pending;
    public DateTimeOffset? CommittedAt { get; set; }
}
