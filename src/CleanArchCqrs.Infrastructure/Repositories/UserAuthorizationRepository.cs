using CleanArchCqrs.Application.Common.Interfaces.Repositorires;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Infrastructure.Repositories
{
    public sealed class UserAuthorizationRepository : IUserAuthorizationRepository
    {
        private readonly AppDbContext _context;
        public UserAuthorizationRepository(AppDbContext context)
        {
            _context = context;
        }
        public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserRoles
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToListAsync(cancellationToken);
        }

        public async Task<string?> GetRoleAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
