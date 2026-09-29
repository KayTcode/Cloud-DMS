using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class Permission : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    public Permission() { }

    public Permission(Guid id, string name, string? description) : base(id)
    {
        Name = name;
        Description = description;
    }
}
