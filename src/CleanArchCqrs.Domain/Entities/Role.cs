using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class Role : BaseEntity
{
    public Guid? TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Tenant? Tenant { get; set; }

    public ICollection<UserRole> UserRoles { get; set; }
        = new List<UserRole>();

    public ICollection<RolePermission> RolePermissions { get; set; }
        = new List<RolePermission>();
}
