using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Common.Interfaces.Repositories
{
    public interface IUserAuthorizationRepository
    {
        Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<string?> GetRoleAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
