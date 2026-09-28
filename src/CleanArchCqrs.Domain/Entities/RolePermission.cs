using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class RolePermission : BaseEntity
    {
        public Guid RoleId { get; private set; }

        public Guid PermissionId { get; private set; }

        public Role Role { get; private set; } = null!;

        public Permission Permission { get; private set; } = null!;
    }
}
