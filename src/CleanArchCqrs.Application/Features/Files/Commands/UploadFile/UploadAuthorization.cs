using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public sealed record UploadAuthorizationProof(AccessEvaluationInput Input,
    CurrentFileAcl? Folder, FileAccessGrant? File);

public sealed class UploadAuthorization(
    IApplicationDbContext db, IFileAccessContextResolver resolver,
    IAccessSnapshotStore snapshots, IFileAclQuery acl,
    IFileAccessService fileAccess, AccessPolicyEvaluator evaluator)
{
    public async Task<(FileAccessContext Context, User User, StorageProvider Provider,
        UploadAuthorizationProof Proof)> RequireAsync(
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

        CurrentFileAcl? folderProof = null;
        if (folderId.HasValue)
        {
            var folder = await acl.ResolveFolderAsync(context, folderId.Value, DateTimeOffset.UtcNow, ct);
            if (folder is null || evaluator.Evaluate(input with
            {
                Resource = folder.Resource, Grants = folder.Grants,
                Target = new("folder", folderId.Value), ServerTime = DateTimeOffset.UtcNow
            }, PermissionNames.FileUpdate).Decision != AuthorizationDecision.Allow)
                throw new AuthorizationChangedException();
            folderProof = folder;
        }
        FileAccessGrant? fileProof = null;
        if (replacingFileId.HasValue)
        {
            fileProof = await fileAccess.CheckAsync(context,
                replacingFileId.Value, PermissionNames.FileUpdate, ct);
            if (fileProof is null || fileProof.Stamp.Generation != input.Generation
                || fileProof.Stamp.UserVersion != input.User.Version
                || !fileProof.Stamp.ScopeVersions.SequenceEqual(input.Scopes
                    .OrderBy(s => s.Key, StringComparer.Ordinal)
                    .Select(s => new ScopeVersionStamp(s.Key, s.Version))))
                throw new AuthorizationChangedException();
        }

        var provider = await (from t in db.Tenants.IgnoreQueryFilters()
            join p in db.StorageProviders.IgnoreQueryFilters() on t.StorageProviderId equals p.Id
            where t.Id == context.TenantId && p.IsActive && p.TotalCapacityBytes > 0
            select p).SingleOrDefaultAsync(ct);
        if (provider is null)
            throw new InvalidOperationException("No active storage provider with a finite quota is configured.");
        return (context, user, provider, new UploadAuthorizationProof(input, folderProof, fileProof));
    }

    public async Task AssertCurrentAsync(FileAccessContext context, User user,
        UploadAuthorizationProof proof, CancellationToken ct)
    {
        var prior = proof.Input;
        var raw = await snapshots.ReadAsync(context.Actor, new("none", null), ct);
        if (!raw.SnapshotsComplete || raw.Scopes.Count == 0
            || raw.Scopes.Count != prior.Scopes.Count || raw.Scopes.Any(s => !s.Complete)
            || raw.Generation != prior.Generation
            || raw.User?.UserId != user.Id || raw.User.TenantId != context.TenantId
            || raw.User.IsActive != true || raw.User.Version != prior.User?.Version
            || raw.Session is not { IsActive: true, Complete: true }
            || raw.Session.UserId != user.Id || raw.Session.SessionId != context.Actor.SessionId
            || raw.Session.Version != prior.Session?.Version || raw.Session.ExpiresAt <= raw.ServerTime)
            throw new AuthorizationChangedException();
        foreach (var scope in raw.Scopes)
        {
            var old = prior.Scopes.SingleOrDefault(s => s.Key == scope.Key);
            if (old is null || !(scope.Version == old.Version && !scope.IsUpdating
                || scope.Key == "dms:scope:global" && scope.IsUpdating
                && scope.Version == old.Version + 1))
                throw new AuthorizationChangedException();
        }
        var currentUser = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == user.Id && u.TenantId == context.TenantId, ct);
        var session = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == context.Actor.SessionId && s.UserId == user.Id, ct);
        if (currentUser is null || !currentUser.IsActive
            || currentUser.AuthorizationVersion != user.AuthorizationVersion
            || session is null || !session.IsActive)
            throw new AuthorizationChangedException();
        var stamps = prior.Scopes.Select(s => new ScopeVersionStamp(s.Key, s.Version)).ToArray();
        var keys = stamps.Select(s => s.Key.Replace("dms:scope:", "")).ToArray();
        var scopes = await db.AuthorizationScopes.AsNoTracking()
            .Where(s => keys.Contains(s.ScopeKey)).ToArrayAsync(ct);
        foreach (var stamp in stamps)
        {
            var scope = scopes.SingleOrDefault(s => s.ScopeKey == stamp.Key.Replace("dms:scope:", ""));
            if ((scope?.Version ?? 0) != stamp.Version || scope?.IsUpdating == true)
                throw new AuthorizationChangedException();
        }
        var now = DateTimeOffset.UtcNow;
        if (proof.Folder is { } oldFolder)
        {
            var folder = await acl.ResolveFolderAsync(context, oldFolder.Resource.Id, now, ct);
            if (folder is null || folder.Resource.Version != oldFolder.Resource.Version
                || folder.Fingerprint != oldFolder.Fingerprint
                || evaluator.Evaluate(prior with { Resource = folder.Resource, Grants = folder.Grants,
                    Target = new("folder", folder.Resource.Id), ServerTime = now },
                    PermissionNames.FileUpdate).Decision != AuthorizationDecision.Allow)
                throw new AuthorizationChangedException();
        }
        if (proof.File is { } oldFile)
        {
            var rows = await acl.ResolveManyAsync(context, new[] { oldFile.FileId }, now, ct);
            if (!rows.TryGetValue(oldFile.FileId, out var file)
                || file.DocumentVersion != oldFile.DocumentVersion
                || file.Resource.Version != oldFile.AuthorizationVersion
                || file.Fingerprint != oldFile.AclFingerprint
                || evaluator.Evaluate(prior with { Resource = file.Resource, Grants = file.Grants,
                    Target = new("file", oldFile.FileId), ServerTime = now },
                    PermissionNames.FileUpdate).Decision != AuthorizationDecision.Allow)
                throw new AuthorizationChangedException();
        }
    }
}
