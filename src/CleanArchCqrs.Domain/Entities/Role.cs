using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Role : BaseEntity
    {
        public string Name { get; private set; } = string.Empty;

        public string? Description { get; private set; }

        public Tenant? Tenant { get; private set; }

        public ICollection<UserRole> UserRoles { get; private set; }
            = new List<UserRole>();

        public ICollection<RolePermission> RolePermissions { get; private set; }
            = new List<RolePermission>();
        private Role() { }
        public Role(Guid id, string name, string? description) : base(id)
        {
            Name = name;
            Description = description;
        }
    }
}
