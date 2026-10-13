using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public sealed class UploadAuthorization(
    IApplicationDbContext db, IFileAccessContextResolver resolver,
    IAccessSnapshotStore snapshots, IFileAclQuery acl,
    IFileAccessService fileAccess, AccessPolicyEvaluator evaluator)
{
    public async Task<(FileAccessContext Context, User User, StorageProvider Provider,
        IReadOnlyList<ScopeVersionStamp> Scopes)> RequireAsync(
        Guid? folderId, Guid? replacingFileId, CancellationToken ct)
    {
        var context = await resolver.ResolveContextAsync(ct);
        var input = await snapshots.ReadAsync(context.Actor, new("none", null), ct);
        var tenantResource = new ResourceAccessSnapshot(context.TenantId, context.TenantId,
            null, Guid.Empty, FileLifecycle.Active, 0);
        var decision = evaluator.Evaluate(input with
        {
            Resource = tenantResource, Target = new("tenant", context.TenantId)
        }, PermissionNames.FileUpload).Decision;
        if (decision == AuthorizationDecision.Unavailable)
            throw new SecurityUnavailableException();
        if (decision != AuthorizationDecision.Allow || input.User?.UserId != context.Actor.UserId
            || input.User.TenantId != context.TenantId || input.Session?.SessionId != context.Actor.SessionId)
            throw new AuthorizationChangedException();

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == context.Actor.UserId && u.TenantId == context.TenantId
                && u.IsActive, ct) ?? throw new AuthorizationChangedException();
        var session = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == context.Actor.SessionId && s.UserId == user.Id, ct);
        if (session is null || !session.IsActive || user.AuthorizationVersion != input.User.Version)
            throw new AuthorizationChangedException();

        if (folderId.HasValue)
        {
            var folder = await acl.ResolveFolderAsync(context, folderId.Value, DateTimeOffset.UtcNow, ct);
            if (folder is null || evaluator.Evaluate(input with
            {
                Resource = folder.Resource, Grants = folder.Grants,
                Target = new("folder", folderId.Value), ServerTime = DateTimeOffset.UtcNow
            }, PermissionNames.FileUpdate).Decision != AuthorizationDecision.Allow)
                throw new AuthorizationChangedException();
        }
        if (replacingFileId.HasValue && await fileAccess.CheckAsync(context,
            replacingFileId.Value, PermissionNames.FileUpdate, ct) is null)
            throw new AuthorizationChangedException();

        var provider = await (from t in db.Tenants.IgnoreQueryFilters()
            join p in db.StorageProviders.IgnoreQueryFilters() on t.StorageProviderId equals p.Id
            where t.Id == context.TenantId && p.IsActive && p.TotalCapacityBytes > 0
            select p).SingleOrDefaultAsync(ct);
        if (provider is null)
            throw new InvalidOperationException("No active storage provider with a finite quota is configured.");
        return (context, user, provider,
            input.Scopes.Select(s => new ScopeVersionStamp(s.Key, s.Version)).ToArray());
    }

    public async Task AssertCurrentAsync(FileAccessContext context, User user,
        IReadOnlyList<ScopeVersionStamp> stamps, CancellationToken ct)
    {
        var currentUser = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == user.Id && u.TenantId == context.TenantId, ct);
        var session = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == context.Actor.SessionId && s.UserId == user.Id, ct);
        if (currentUser is null || !currentUser.IsActive
            || currentUser.AuthorizationVersion != user.AuthorizationVersion
            || session is null || !session.IsActive)
            throw new AuthorizationChangedException();
        var keys = stamps.Select(s => s.Key.Replace("dms:scope:", "")).ToArray();
        var scopes = await db.AuthorizationScopes.AsNoTracking()
            .Where(s => keys.Contains(s.ScopeKey)).ToArrayAsync(ct);
        foreach (var stamp in stamps)
        {
            var scope = scopes.SingleOrDefault(s => s.ScopeKey == stamp.Key.Replace("dms:scope:", ""));
            if (scope is null || scope.Version != stamp.Version || scope.IsUpdating)
                throw new AuthorizationChangedException();
        }
    }
}
