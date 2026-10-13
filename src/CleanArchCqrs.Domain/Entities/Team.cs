using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class Team : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long AuthorizationVersion { get; set; } = 1;
}
