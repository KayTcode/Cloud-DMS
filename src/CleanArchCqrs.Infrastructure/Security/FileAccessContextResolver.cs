using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class FileAccessContextResolver(
    ICurrentUserService currentUser, AppDbContext db, IAccessSnapshotStore snapshots) : IFileAccessContextResolver
{
    public Task<FileAccessContext> ResolveContextForMutationReceiptAsync(CancellationToken ct) => ResolveAsync(true, ct);
    public Task<FileAccessContext> ResolveContextAsync(CancellationToken ct) => ResolveAsync(false, ct);

    private async Task<FileAccessContext> ResolveAsync(bool allowPendingReceipt, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty
            || currentUser.SessionId is not { } sessionId || sessionId == Guid.Empty)
            throw new UnauthorizedAccessException("A valid authenticated session is required.");
        var session = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (session is null || !session.IsActive)
            throw new UnauthorizedAccessException("The session is inactive.");

        var actor = new ActorIdentity(userId, sessionId);
        var snapshot = await snapshots.ReadAsync(actor, new AuthorizationTarget("none", null), ct);
        if (!snapshot.SnapshotsComplete || string.IsNullOrEmpty(snapshot.Generation) || snapshot.Scopes.Count == 0
            || snapshot.Scopes.Any(x => !x.Complete)
            || !allowPendingReceipt && snapshot.Scopes.Any(x => x.IsUpdating))
            throw new SecurityUnavailableException();
        if (snapshot.User is not { IsActive: true, TenantId: { } tenantId } || tenantId == Guid.Empty
            || snapshot.User.UserId != userId || snapshot.Session is not { IsActive: true, Complete: true }
            || snapshot.Session.UserId != userId || snapshot.Session.SessionId != sessionId
            || snapshot.Session.ExpiresAt <= snapshot.ServerTime)
            throw new UnauthorizedAccessException("The session has no active tenant context.");
        return new FileAccessContext(actor, tenantId);
    }
}
