using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class FileAccessContextResolver(ICurrentUserService currentUser, AppDbContext db, IAccessSnapshotStore snapshots) : IFileAccessContextResolver
{
    public async Task<FileAccessContext> ResolveContextAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty
            || currentUser.SessionId is not { } sessionId || sessionId == Guid.Empty)
            throw new UnauthorizedAccessException("A valid authenticated session is required.");
        var session = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, cancellationToken);
        if (session is null || !session.IsActive) throw new UnauthorizedAccessException("The session is inactive.");
        var actor = new ActorIdentity(userId, sessionId);
        var snapshot = await snapshots.ReadAsync(actor, new AuthorizationTarget("none", null), cancellationToken);
        if (!snapshot.SnapshotsComplete || snapshot.Scopes.Any(x => x.IsUpdating)) throw new SecurityUnavailableException();
        if (snapshot.User is not { IsActive: true, TenantId: { } tenantId } || tenantId == Guid.Empty
            || snapshot.Session is not { IsActive: true } || snapshot.Session.ExpiresAt <= snapshot.ServerTime)
            throw new UnauthorizedAccessException("The session has no active tenant context.");
        return new FileAccessContext(actor, tenantId);
    }
}
