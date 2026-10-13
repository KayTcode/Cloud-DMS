using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;

public sealed class FileMutationService(
    IApplicationDbContext db,
    IFileAccessContextResolver resolver,
    IAccessSnapshotStore snapshots,
    IFileAccessService access,
    IFileAclQuery acl,
    AccessPolicyEvaluator evaluator,
    IAuthorizationMutationCoordinator coordinator,
    IQuotaReleaseWriter quotaReleaseWriter)
{
    public async Task<MutationReceipt> ChangeLifecycleAsync(
        Guid id, FileLifecycle lifecycle, string idempotencyKey, CancellationToken ct)
    {
        if (id == Guid.Empty || lifecycle is not (FileLifecycle.Active or FileLifecycle.Binned or FileLifecycle.Purging))
            throw new ArgumentException("Invalid lifecycle mutation.");

        var context = await resolver.ResolveContextForMutationReceiptAsync(ct);
        var request = CreateRequest(context, idempotencyKey, new { Kind = "Lifecycle", FileId = id, Lifecycle = lifecycle });
        var existing = await ReplayAsync(context, request, ct);
        if (existing is not null)
            return existing;

        context = await resolver.ResolveContextAsync(ct);
        var action = lifecycle switch
        {
            FileLifecycle.Binned => PermissionNames.FileDelete,
            FileLifecycle.Active => PermissionNames.FileRestore,
            _ => PermissionNames.FilePurge
        };
        var grant = await access.CheckAsync(context, id, action, ct);
        if (grant is null && lifecycle == FileLifecycle.Purging)
            grant = await access.CheckAsync(context, id, PermissionNames.BinPurge, ct);
        if (grant is null)
            throw new AuthorizationChangedException();

        var input = await CaptureAsync(context, ct);
        if (input.Generation != grant.Stamp.Generation || input.User!.Version != grant.Stamp.UserVersion
            || !ScopeStamps(input).SequenceEqual(grant.Stamp.ScopeVersions))
            throw new AuthorizationChangedException();

        return await coordinator.ExecuteAsync(request, context.Actor.UserId, async token =>
        {
            await AssertContextCurrentAsync(context, input, token);
            await AssertFileCurrentAsync(context, input, grant, token);
            var file = await db.FileEntries.IgnoreQueryFilters()
                .SingleAsync(f => f.TenantId == context.TenantId && f.Id == id, token);
            if (lifecycle == FileLifecycle.Binned && file.Lifecycle != FileLifecycle.Active
                || lifecycle != FileLifecycle.Binned && file.Lifecycle != FileLifecycle.Binned)
                throw new AuthorizationChangedException();

            file.Lifecycle = lifecycle;
            file.IsDeleted = lifecycle != FileLifecycle.Active;
            file.AuthorizationVersion = checked(file.AuthorizationVersion + 1);
            file.ActiveSearchGenerationId = null;
            file.DeletedAt = file.IsDeleted ? DateTime.UtcNow : null;
            file.DeletedByUserId = file.IsDeleted ? context.Actor.UserId : null;
            if (lifecycle == FileLifecycle.Purging)
            {
                if (!file.QuotaReleasedAt.HasValue)
                {
                    await quotaReleaseWriter.ReleaseAsync(file, token);
                    file.QuotaReleasedAt = DateTimeOffset.UtcNow;
                }
            }

            db.DocumentOutboxMessages.Add(new()
            {
                TenantId = context.TenantId,
                FileId = id,
                DocumentVersion = file.DocumentVersion,
                EventType = lifecycle switch
                {
                    FileLifecycle.Active => "DocumentRestored",
                    FileLifecycle.Binned => "DocumentBinned",
                    _ => "DocumentDeleted"
                },
                IdempotencyKey = request.IdempotencyKey,
                Payload = JsonSerializer.Serialize(new
                {
                    file.Id, file.DocumentVersion,
                    AuthorizationVersion = file.AuthorizationVersion,
                    Lifecycle = lifecycle.ToString()
                })
            });
            Audit(context, "FileLifecycle", id);
        }, ct);
    }

    public async Task<MutationReceipt> ManageAsync(ManageAccessCommand command, CancellationToken ct)
    {
        if (command.ResourceId == Guid.Empty || !Enum.IsDefined(command.Kind)
            || !Enum.IsDefined(command.PrincipalKind) || !Enum.IsDefined(command.Role))
            throw new ArgumentException("Invalid access mutation.");

        var context = await resolver.ResolveContextForMutationReceiptAsync(ct);
        var request = CreateRequest(context, command.IdempotencyKey, command with { IdempotencyKey = "" });
        var existing = await ReplayAsync(context, request, ct);
        if (existing is not null)
            return existing;

        if (command.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Invalid access mutation.");
        context = await resolver.ResolveContextAsync(ct);
        var input = await CaptureAsync(context, ct);
        FileAccessGrant? grant = null;
        CurrentFileAcl? folderAuthority = null;
        CurrentFileAcl? destinationAuthority = null;
        if (command.Kind is AccessMutationKind.UpsertFileGrant or AccessMutationKind.RevokeFileGrant)
        {
            grant = await access.CheckAsync(context, command.ResourceId, PermissionNames.FileShare, ct);
            if (grant is null)
                throw new AuthorizationChangedException();
            if (input.Generation != grant.Stamp.Generation || input.User!.Version != grant.Stamp.UserVersion
                || !ScopeStamps(input).SequenceEqual(grant.Stamp.ScopeVersions))
                throw new AuthorizationChangedException();
        }
        else
        {
            var teamMutation = command.Kind is AccessMutationKind.AddTeamMember or AccessMutationKind.RemoveTeamMember;
            if (teamMutation)
                AssertStructureAuthority(context, input, PermissionNames.UserManage);
            else
            {
                folderAuthority = await RequireFolderOwnerAsync(context, input, command.ResourceId, ct);
                if (command.Kind == AccessMutationKind.MoveFolder && command.ParentFolderId.HasValue)
                    destinationAuthority = await RequireFolderOwnerAsync(context, input, command.ParentFolderId.Value, ct);
            }
        }

        return await coordinator.ExecuteAsync(request, context.Actor.UserId, async token =>
        {
            await AssertContextCurrentAsync(context, input, token);
            if (grant is not null)
                await AssertFileCurrentAsync(context, input, grant, token);
            if (folderAuthority is not null)
                await AssertFolderCurrentAsync(context, input, folderAuthority, token);
            if (destinationAuthority is not null)
                await AssertFolderCurrentAsync(context, input, destinationAuthority, token);

            if (command.Kind == AccessMutationKind.MoveFolder)
                await MoveFolderAsync(context, command, token);
            else if (command.Kind is AccessMutationKind.AddTeamMember or AccessMutationKind.RemoveTeamMember)
                await TeamMemberAsync(context, command, token);
            else
                await ResourceGrantAsync(context, command, token);
            Audit(context, command.Kind.ToString(), command.ResourceId);
        }, ct);
    }

    private async Task<AccessEvaluationInput> CaptureAsync(FileAccessContext context, CancellationToken ct)
    {
        var input = await snapshots.ReadAsync(context.Actor, new("none", null), ct);
        var decision = evaluator.Evaluate(input, "context.validate").Decision;
        if (decision == AuthorizationDecision.Unavailable)
            throw new SecurityUnavailableException();
        if (decision == AuthorizationDecision.Unauthenticated || input.User?.UserId != context.Actor.UserId
            || input.User?.TenantId != context.TenantId || input.User?.IsActive != true
            || input.Session?.SessionId != context.Actor.SessionId)
            throw new AuthorizationChangedException();
        return input;
    }

    private async Task AssertContextCurrentAsync(FileAccessContext context, AccessEvaluationInput input, CancellationToken ct)
    {
        // The raw atomic envelope remains readable while our barrier blocks content evaluation.
        // A blocked session or changed generation must still reject this callback.
        var currentSnapshot = await snapshots.ReadAsync(context.Actor, new("none", null), ct);
        if (!currentSnapshot.SnapshotsComplete || currentSnapshot.Scopes.Any(s => !s.Complete)
            || currentSnapshot.Scopes.Count == 0)
            throw new SecurityUnavailableException();
        if (currentSnapshot.Generation != input.Generation || currentSnapshot.User?.UserId != context.Actor.UserId
            || currentSnapshot.User?.TenantId != context.TenantId || currentSnapshot.User?.IsActive != true
            || currentSnapshot.User?.Version != input.User?.Version
            || currentSnapshot.Session is not { IsActive: true, Complete: true }
            || currentSnapshot.Session.UserId != context.Actor.UserId
            || currentSnapshot.Session.SessionId != context.Actor.SessionId
            || currentSnapshot.Session.Version != input.Session?.Version
            || currentSnapshot.Session.ExpiresAt <= currentSnapshot.ServerTime)
            throw new AuthorizationChangedException();
        foreach (var scope in currentSnapshot.Scopes)
        {
            var prior = input.Scopes.SingleOrDefault(s => s.Key == scope.Key);
            if (prior is null || !(scope.Version == prior.Version && !scope.IsUpdating
                || scope.Key == "dms:scope:global" && scope.IsUpdating && scope.Version == prior.Version + 1))
                throw new AuthorizationChangedException();
        }

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == context.Actor.UserId && u.TenantId == context.TenantId, ct);
        var session = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == context.Actor.SessionId && s.UserId == context.Actor.UserId, ct);
        if (user is null || !user.IsActive || user.AuthorizationVersion != input.User!.Version
            || session is null || session.RevokedAt.HasValue || session.ExpiresAt <= DateTime.UtcNow)
            throw new AuthorizationChangedException();

        var keys = input.Scopes.Select(s => s.Key.Replace("dms:scope:", "")).ToArray();
        var scopes = await db.AuthorizationScopes.AsNoTracking().Where(s => keys.Contains(s.ScopeKey)).ToArrayAsync(ct);
        foreach (var stamp in input.Scopes)
        {
            var current = scopes.SingleOrDefault(s => s.ScopeKey == stamp.Key.Replace("dms:scope:", ""));
            if ((current?.Version ?? 0) != stamp.Version || current?.IsUpdating == true)
                throw new AuthorizationChangedException();
        }
    }

    private async Task AssertFileCurrentAsync(
        FileAccessContext context, AccessEvaluationInput input, FileAccessGrant grant, CancellationToken ct)
    {
        var rows = await acl.ResolveManyAsync(context, new[] { grant.FileId }, DateTimeOffset.UtcNow, ct);
        if (!rows.TryGetValue(grant.FileId, out var row) || row.DocumentVersion != grant.DocumentVersion
            || row.Resource.Version != grant.AuthorizationVersion || row.Fingerprint != grant.AclFingerprint)
            throw new AuthorizationChangedException();
        var current = evaluator.Evaluate(input with
        {
            Resource = row.Resource, Grants = row.Grants,
            ServerTime = DateTimeOffset.UtcNow, Target = new("file", grant.FileId)
        }, grant.Stamp.Action);
        if (current.Decision != AuthorizationDecision.Allow || current.Stamp is null
            || !FileAccessService.SameStamp(grant.Stamp, current.Stamp))
            throw new AuthorizationChangedException();
    }

    private void AssertStructureAuthority(FileAccessContext context, AccessEvaluationInput input, string action)
    {
        if (!input.User!.Roles.Contains("TenantAdmin", StringComparer.OrdinalIgnoreCase)
            || evaluator.Evaluate(input with
            {
                Resource = new(context.TenantId, context.TenantId, null, Guid.Empty, FileLifecycle.Active, 0),
                Target = new("tenant", context.TenantId)
            }, action).Decision != AuthorizationDecision.Allow)
            throw new AuthorizationChangedException();
    }

    private async Task<CurrentFileAcl> RequireFolderOwnerAsync(
        FileAccessContext context, AccessEvaluationInput input, Guid id, CancellationToken ct)
    {
        var folder = await acl.ResolveFolderAsync(context, id, DateTimeOffset.UtcNow, ct);
        if (folder is null || evaluator.Evaluate(input with
        {
            Resource = folder.Resource, Grants = folder.Grants,
            Target = new("folder", id), ServerTime = DateTimeOffset.UtcNow
        }, PermissionNames.FileShare).Decision != AuthorizationDecision.Allow)
            throw new AuthorizationChangedException();
        return folder;
    }

    private async Task AssertFolderCurrentAsync(
        FileAccessContext context, AccessEvaluationInput input, CurrentFileAcl old, CancellationToken ct)
    {
        var current = await RequireFolderOwnerAsync(context, input, old.Resource.Id, ct);
        if (current.Resource.Version != old.Resource.Version || current.Fingerprint != old.Fingerprint)
            throw new AuthorizationChangedException();
    }

    private async Task MoveFolderAsync(FileAccessContext context, ManageAccessCommand command, CancellationToken ct)
    {
        var folders = await db.Folders.IgnoreQueryFilters().Where(f => f.TenantId == context.TenantId)
            .ToDictionaryAsync(f => f.Id, ct);
        if (!folders.TryGetValue(command.ResourceId, out var folder))
            throw new AuthorizationChangedException();
        var seen = new HashSet<Guid> { folder.Id };
        var parentId = command.ParentFolderId;
        while (parentId.HasValue)
        {
            if (!seen.Add(parentId.Value) || !folders.TryGetValue(parentId.Value, out var parent))
                throw new AuthorizationChangedException();
            parentId = parent.ParentFolderId;
        }
        folder.ParentFolderId = command.ParentFolderId;
        folder.AuthorizationVersion = checked(folder.AuthorizationVersion + 1);
    }

    private async Task TeamMemberAsync(FileAccessContext context, ManageAccessCommand command, CancellationToken ct)
    {
        var team = await db.Teams.IgnoreQueryFilters()
            .SingleOrDefaultAsync(t => t.TenantId == context.TenantId && t.Id == command.ResourceId, ct);
        if (team is null || !await db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == context.TenantId && u.Id == command.PrincipalId, ct))
            throw new AuthorizationChangedException();
        var member = await db.TeamMembers.IgnoreQueryFilters().SingleOrDefaultAsync(
            m => m.TenantId == context.TenantId && m.TeamId == team.Id && m.UserId == command.PrincipalId, ct);
        if (command.Kind == AccessMutationKind.AddTeamMember)
        {
            if (member is null)
                db.TeamMembers.Add(new() { TenantId = context.TenantId, TeamId = team.Id, UserId = command.PrincipalId });
        }
        else if (member is not null)
            db.TeamMembers.Remove(member);
        team.AuthorizationVersion = checked(team.AuthorizationVersion + 1);
    }

    private async Task ResourceGrantAsync(FileAccessContext context, ManageAccessCommand command, CancellationToken ct)
    {
        var fileKind = command.Kind is AccessMutationKind.UpsertFileGrant or AccessMutationKind.RevokeFileGrant;
        var resourceKind = fileKind ? ResourceKind.File : ResourceKind.Folder;
        var principalExists = command.PrincipalKind == PrincipalKind.User
            ? await db.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == context.TenantId && u.Id == command.PrincipalId, ct)
            : await db.Teams.IgnoreQueryFilters().AnyAsync(t => t.TenantId == context.TenantId && t.Id == command.PrincipalId, ct);
        if (command.PrincipalId == Guid.Empty || !principalExists)
            throw new AuthorizationChangedException();
        if (fileKind)
        {
            var file = await db.FileEntries.IgnoreQueryFilters()
                .SingleAsync(f => f.TenantId == context.TenantId && f.Id == command.ResourceId, ct);
            file.AuthorizationVersion = checked(file.AuthorizationVersion + 1);
        }
        else
        {
            var folder = await db.Folders.IgnoreQueryFilters()
                .SingleOrDefaultAsync(f => f.TenantId == context.TenantId && f.Id == command.ResourceId, ct);
            if (folder is null)
                throw new AuthorizationChangedException();
            folder.AuthorizationVersion = checked(folder.AuthorizationVersion + 1);
        }
        var grants = await db.ResourceGrants.IgnoreQueryFilters().Where(g => g.TenantId == context.TenantId
            && g.ResourceKind == resourceKind && g.ResourceId == command.ResourceId
            && g.PrincipalKind == command.PrincipalKind && g.PrincipalId == command.PrincipalId).ToArrayAsync(ct);
        db.ResourceGrants.RemoveRange(grants);
        if (command.Kind is AccessMutationKind.UpsertFileGrant or AccessMutationKind.UpsertFolderGrant)
            db.ResourceGrants.Add(new()
            {
                TenantId = context.TenantId, ResourceKind = resourceKind, ResourceId = command.ResourceId,
                PrincipalKind = command.PrincipalKind, PrincipalId = command.PrincipalId,
                Role = command.Role, ExpiresAt = command.ExpiresAt
            });
        if (fileKind && command.PrincipalKind == PrincipalKind.User)
        {
            var legacy = await db.FileShares.IgnoreQueryFilters().Where(s => s.FileEntryId == command.ResourceId
                && s.SharedWithUserId == command.PrincipalId).ToArrayAsync(ct);
            db.FileShares.RemoveRange(legacy);
        }
    }

    private static MutationRequest CreateRequest(FileAccessContext context, string key, object payload)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new ArgumentException("A valid Idempotency-Key is required.");
        return new("global", Hash($"file-mutation:{context.Actor.UserId:N}:{key}"), Hash(JsonSerializer.Serialize(payload)));
    }

    private async Task<MutationReceipt?> ReplayAsync(FileAccessContext context, MutationRequest request, CancellationToken ct)
    {
        var old = await db.AuthorizationOperations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.ScopeKey == "global" && o.IdempotencyKey == request.IdempotencyKey, ct);
        if (old is null)
            return null;
        if (old.InitiatedByUserId != context.Actor.UserId || old.PayloadHash != request.PayloadHash)
            throw new AuthorizationMutationConflictException();
        return new(old.Id, old.Version, old.Status);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ScopeVersionStamp[] ScopeStamps(AccessEvaluationInput input) =>
        input.Scopes.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => new ScopeVersionStamp(s.Key, s.Version)).ToArray();

    private void Audit(FileAccessContext context, string action, Guid id) => db.AuditLogs.Add(new()
    {
        TenantId = context.TenantId, UserId = context.Actor.UserId, Action = action,
        EntityName = "FileAccess", EntityId = id, Details = "Thay đổi trạng thái/quyền tài liệu."
    });
}
