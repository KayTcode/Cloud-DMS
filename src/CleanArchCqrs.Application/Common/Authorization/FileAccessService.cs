using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Authorization;

public sealed class FileAccessService(
    IAccessSnapshotStore snapshots, IFileAclQuery acl, AccessPolicyEvaluator evaluator) : IFileAccessService
{
    public Task<IReadOnlyDictionary<Guid, FileAccessGrant>> CheckManyAsync(
        FileAccessContext context, IReadOnlyCollection<Guid> fileIds, CancellationToken ct) =>
        CheckBatchAsync(context, fileIds, PermissionNames.FileRead, ct);

    public async Task<FileAccessGrant?> CheckAsync(
        FileAccessContext context, Guid id, string action, CancellationToken ct) =>
        (await CheckBatchAsync(context, new[] { id }, action, ct)).GetValueOrDefault(id);

    private async Task<IReadOnlyDictionary<Guid, FileAccessGrant>> CheckBatchAsync(
        FileAccessContext context, IReadOnlyCollection<Guid> ids, string action, CancellationToken ct)
    {
        var before = await ReadContextAsync(context, ct);
        var rows = await acl.ResolveManyAsync(context, ids, before.ServerTime, ct);
        var after = await ReadContextAsync(context, ct);
        if (!SameContext(before, after))
            throw new AuthorizationChangedException();
        var result = new Dictionary<Guid, FileAccessGrant>();
        foreach (var (id, row) in rows)
        {
            if (!ids.Contains(id) || row.Resource.TenantId != context.TenantId
                || action == PermissionNames.FileRead && !row.VerifiedClean)
                continue;
            var input = after with { Resource = row.Resource, Grants = row.Grants, Target = new("file", id) };
            var evaluated = evaluator.Evaluate(input, action);
            if (evaluated.Decision == AuthorizationDecision.Allow && evaluated.Stamp is not null)
                result[id] = new(id, row.DocumentVersion, row.Resource.Version, evaluated.Stamp, row.Fingerprint);
        }
        return result;
    }

    public async Task AssertStillValidAsync(
        FileAccessContext context, IReadOnlyCollection<FileAccessGrant> grants, CancellationToken ct)
    {
        foreach (var group in grants.GroupBy(g => g.Stamp.Action))
        {
            var current = await CheckBatchAsync(context, group.Select(g => g.FileId).Distinct().ToArray(), group.Key, ct);
            foreach (var old in group)
            {
                if (!current.TryGetValue(old.FileId, out var value)
                    || old.DocumentVersion != value.DocumentVersion
                    || old.AuthorizationVersion != value.AuthorizationVersion
                    || old.AclFingerprint != value.AclFingerprint
                    || !SameStamp(old.Stamp, value.Stamp))
                    throw new AuthorizationChangedException();
            }
        }
    }

    private async Task<AccessEvaluationInput> ReadContextAsync(FileAccessContext context, CancellationToken ct)
    {
        var input = await snapshots.ReadAsync(context.Actor, new("none", null), ct);
        // Unknown action still runs snapshot/session validation before denying.
        var decision = evaluator.Evaluate(input, "context.validate").Decision;
        if (decision == AuthorizationDecision.Unavailable)
            throw new SecurityUnavailableException();
        if (decision == AuthorizationDecision.Unauthenticated || input.User?.UserId != context.Actor.UserId
            || input.Session?.SessionId != context.Actor.SessionId || input.User?.TenantId != context.TenantId
            || input.User?.IsActive != true)
            throw new AuthorizationChangedException();
        return input;
    }

    private static bool SameContext(AccessEvaluationInput a, AccessEvaluationInput b) =>
        a.Generation == b.Generation && a.User?.Version == b.User?.Version && a.Session?.Version == b.Session?.Version
        && a.Scopes.OrderBy(s => s.Key).SequenceEqual(b.Scopes.OrderBy(s => s.Key));

    public static bool SameStamp(AuthorizationStamp a, AuthorizationStamp b) =>
        a.Generation == b.Generation && a.Action == b.Action && a.SessionId == b.SessionId
        && a.UserVersion == b.UserVersion && a.SessionVersion == b.SessionVersion
        && Equals(a.Target, b.Target) && a.ResourceVersion == b.ResourceVersion
        && a.ScopeVersions.SequenceEqual(b.ScopeVersions);
}
