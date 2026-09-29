using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Domain.Entities;

public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public Role Role { get; set; } = null!;

        public Permission Permission { get; private set; } = null!;
        private RolePermission() { }
        public RolePermission(Guid id, Guid roleId, Guid permissionId) : base(id)
        {
            RoleId = roleId;
            PermissionId = permissionId;
        }
    }
}
