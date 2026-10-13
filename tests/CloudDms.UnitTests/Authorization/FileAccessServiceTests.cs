using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
namespace CloudDms.UnitTests.Authorization;
public class FileAccessServiceTests
{
    private sealed class Snapshots : IAccessSnapshotStore
    {
        public AccessEvaluationInput Input = null!;
        public int Reads;
        public Task<AccessEvaluationInput> ReadAsync(ActorIdentity actor, AuthorizationTarget target, CancellationToken ct) { Reads++; return Task.FromResult(Input); }
    }
    private sealed class Acl : IFileAclQuery
    {
        public Task<CurrentFileAcl?> ResolveFolderAsync(FileAccessContext c, Guid id, DateTimeOffset now, CancellationToken ct) => throw new NotImplementedException();
        public Dictionary<Guid, CurrentFileAcl> Rows = new(); public int Reads;
        public Task<IReadOnlyDictionary<Guid, CurrentFileAcl>> ResolveManyAsync(FileAccessContext c, IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct) { Reads++; return Task.FromResult<IReadOnlyDictionary<Guid, CurrentFileAcl>>(Rows); }
        public Task<IQueryable<CleanArchCqrs.Domain.Entities.FileEntry>> ReadScopeAsync(FileAccessContext c, DateTimeOffset now, CancellationToken ct) => throw new NotImplementedException();
    }
    [Fact]
    public async Task VerifiedReadIsRecheckedAfterRevocationWithConstantSnapshotCalls()
    {
        var tenant = Guid.NewGuid(); var actor = new ActorIdentity(Guid.NewGuid(), Guid.NewGuid()); var context = new FileAccessContext(actor, tenant); var now = DateTimeOffset.UtcNow;
        var snapshots = new Snapshots { Input = new(new(actor.UserId, tenant, null, true, 1, new HashSet<string>(), new HashSet<string> { "file.read" }), new(actor.SessionId, actor.UserId, true, now.AddHours(1), 1, true), null, Array.Empty<ShareGrant>(), new[] { new ScopeAccessSnapshot("global", 1, false, true) }, true, now, "g", new("none", null)) };
        var acl = new Acl(); var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids) acl.Rows[id] = new(new(id, tenant, null, actor.UserId, CleanArchCqrs.Domain.Enums.FileLifecycle.Active, 1), 1, true, Array.Empty<ShareGrant>(), "a");
        var service = new FileAccessService(snapshots, acl, new AccessPolicyEvaluator());
        var grants = await service.CheckManyAsync(context, ids, default);
        Assert.Equal(200, grants.Count); Assert.Equal(2, snapshots.Reads); Assert.Equal(1, acl.Reads);
        acl.Rows.Remove(ids[0]);
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => service.AssertStillValidAsync(context, grants.Values.ToArray(), default));
    }
    [Fact]
    public async Task LegacyCleanAndPendingBarrierDoNotAuthorizeRead()
    {
        var tenant = Guid.NewGuid(); var actor = new ActorIdentity(Guid.NewGuid(), Guid.NewGuid()); var context = new FileAccessContext(actor, tenant); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        var snapshots = new Snapshots { Input = new(new(actor.UserId, tenant, null, true, 1, new HashSet<string>(), new HashSet<string> { "file.read" }), new(actor.SessionId, actor.UserId, true, now.AddHours(1), 1, true), null, Array.Empty<ShareGrant>(), new[] { new ScopeAccessSnapshot("global", 1, false, true) }, true, now, "g", new("none", null)) };
        var acl = new Acl(); acl.Rows[id] = new(new(id, tenant, null, actor.UserId, CleanArchCqrs.Domain.Enums.FileLifecycle.Active, 1), 1, false, Array.Empty<ShareGrant>(), "a");
        var service = new FileAccessService(snapshots, acl, new AccessPolicyEvaluator());
        Assert.Empty(await service.CheckManyAsync(context, new[] { id }, default));
        snapshots.Input = snapshots.Input with { Scopes = new[] { new ScopeAccessSnapshot("global", 2, true, true) } };
        await Assert.ThrowsAsync<SecurityUnavailableException>(() => service.CheckManyAsync(context, new[] { id }, default));
    }
}
