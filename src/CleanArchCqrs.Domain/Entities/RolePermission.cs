using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public Role Role { get; set; } = null!;

    public Permission Permission { get; set; } = null!;

    public RolePermission() { }

    public RolePermission(Guid id, Guid roleId, Guid permissionId) : base(id)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}
