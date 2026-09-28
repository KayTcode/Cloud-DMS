using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Common.Interfaces
{
    public interface IApplicationDbContext
    {
        DbSet<Tenant> Tenants { get; }

        DbSet<Department> Departments { get; }

        DbSet<User> Users { get; }

        DbSet<Role> Roles { get; }

        DbSet<Permission> Permissions { get; }

        DbSet<UserRole> UserRoles { get; }

        DbSet<RolePermission> RolePermissions { get; }

        DbSet<RefreshToken> RefreshTokens { get; }

        DbSet<AuditLog> AuditLogs { get; }

        Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default);
    }
}
