using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class AuditLog : BaseEntity
{
    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Details { get; set; }

    public Tenant? Tenant { get; set; }

    public User? User { get; set; }
}
