using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Security;

/// <summary>Rebuilds complete authoritative snapshots before issuing a session-bearing token.</summary>
public sealed class AuthSessionService(AppDbContext db, ISecurityRedis redis, ISecurityReadiness readiness, IJwtTokenService jwt) : IAuthSessionService
{

    public async Task<JwtTokenResult> IssueAsync(User user, string role, IEnumerable<string> permissions, Guid sessionId, CancellationToken cancellationToken)
    {
        var persisted = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == user.Id, cancellationToken);
        if (persisted is null || !persisted.IsActive || !user.IsActive) throw new UnauthorizedAccessException("The persisted session is inactive.");
        await RebuildAsync(cancellationToken);
        // Validate through the same atomic snapshot path that consumers will use.
        var snapshot = await new RedisAccessSnapshotStore(redis, readiness).ReadAsync(
            new(user.Id, sessionId), new("none", null), cancellationToken);
        if (!snapshot.SnapshotsComplete || snapshot.Scopes.Any(x => x.IsUpdating)) throw new SecurityUnavailableException();
        if (snapshot.User is not { IsActive: true } || snapshot.Session is not { IsActive: true }
            || snapshot.Session.ExpiresAt <= snapshot.ServerTime) throw new UnauthorizedAccessException("The projected session is inactive.");
        return jwt.GenerateAccessToken(user, role, permissions, sessionId);
    }

    public async Task BlockAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await redis.ExecuteAsync("block-session", new[] { $"auth:session:{sessionId:N}" }, Array.Empty<string>(), cancellationToken);
        if (result != "blocked") throw new SecurityUnavailableException();
    }

    public Task RebuildAsync(CancellationToken cancellationToken) => new SecurityProjectionBootstrapper(db, redis, readiness).RebuildAsync(cancellationToken);
}
