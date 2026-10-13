using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class ResourceGrant : BaseEntity
{
    public Guid TenantId { get; set; }
    public ResourceKind ResourceKind { get; set; }
    public Guid ResourceId { get; set; }
    public PrincipalKind PrincipalKind { get; set; }
    public Guid PrincipalId { get; set; }
    public ResourceRole Role { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
