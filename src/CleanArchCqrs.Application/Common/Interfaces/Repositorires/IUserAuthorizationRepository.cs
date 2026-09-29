using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Common.Interfaces.Repositorires
{
    public interface IUserAuthorizationRepository
    {
        Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<string?> GetRoleAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
