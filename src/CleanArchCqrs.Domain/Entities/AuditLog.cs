using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Domain.Entities;

public class AuditLog : BaseEntity
{
    public Guid? TenantId { get; private set; }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityName { get; private set; } = string.Empty;

    public Guid? EntityId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? Details { get; private set; }

    public Tenant? Tenant { get; private set; }

    public User? User { get; private set; }
}
