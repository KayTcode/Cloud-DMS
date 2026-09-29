using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Domain.Entities;

public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public User User { get; set; } = null!;

        public Role Role { get; private set; } = null!;
        private UserRole() { }

        public UserRole(Guid id, Guid userId, Guid roleId) : base(id)
        {
            UserId = userId;
            RoleId = roleId;
        }
    }
}
