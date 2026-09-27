using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class UserRole : BaseEntity
    {
        public Guid UserId { get; private set; }

        public Guid RoleId { get; private set; }

        public User User { get; private set; } = null!;

        public Role Role { get; private set; } = null!;
    }
}
