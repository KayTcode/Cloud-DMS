using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IAuthSessionService
{
    Task<JwtTokenResult> IssueAsync(User user, string role, IEnumerable<string> permissions, Guid sessionId, CancellationToken cancellationToken);
    Task BlockAsync(Guid sessionId, CancellationToken cancellationToken);
}
