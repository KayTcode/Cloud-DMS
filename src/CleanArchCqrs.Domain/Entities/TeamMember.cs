using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class TeamMember : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
}
