using CleanArchCqrs.Domain.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CleanArchCqrs.Application.Common.Interfaces.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<User?> GetByEmailAsync(
            string email,
            CancellationToken cancellationToken = default);

        Task<bool> ExistsByEmailAsync(
            string email,
            CancellationToken cancellationToken = default);

        Task<User?> GetByGoogleSubjectAsync(
            string googleSubject,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            User user,
            CancellationToken cancellationToken = default);

        void Update(User user);
    }
}
