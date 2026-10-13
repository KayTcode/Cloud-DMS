using System.Data.Common;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Services;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace CloudDms.IntegrationTests.Authorization;
[CollectionDefinition("File ACL", DisableParallelization = true)]
public sealed class FileAclCollection : ICollectionFixture<MySqlFixture>, ICollectionFixture<RedisFixture> { }
[Collection("File ACL")]
public sealed class FileAclIntegrationTests(MySqlFixture mysql, RedisFixture redis)
{
    private sealed record Current(Guid? UserId, Guid? SessionId, Guid? TenantId) : ICurrentUserService
    {
        public Guid? DepartmentId => null; public string? Email => null; public string? Role => "Employee"; public bool IsAuthenticated => true;
    }
    private sealed class Counter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default) { Reads++; return ValueTask.FromResult(result); }
    }
    [Fact]
    public async Task MySqlReadScopeAndBatchUseSameExpiredGrantSemanticsAcrossTenantContexts()
    {
        await using var db = mysql.CreateDbContext(); var tenant = new Tenant(Guid.NewGuid(), "ACL SQL", Guid.NewGuid().ToString("N"), 10000); var other = new Tenant(Guid.NewGuid(), "Other", Guid.NewGuid().ToString("N"), 10000); var actor = new User(tenant.Id, null, $"acl-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null); var owner = new User(tenant.Id, null, $"owner-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null); var foreignUser = new User(other.Id, null, $"other-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null); var provider = new StorageProvider { Name = "ACL test", ConfigEncrypted = "fixture", TotalCapacityBytes = 10000 }; var root = new Folder { TenantId = tenant.Id, Name = "root" }; var child = new Folder { TenantId = tenant.Id, Name = "child", ParentFolderId = root.Id }; var team = new Team { TenantId = tenant.Id, Name = "team" };
        db.Tenants.AddRange(tenant, other); db.Users.AddRange(actor, owner, foreignUser); db.StorageProviders.Add(provider); db.Folders.AddRange(root, child); db.Teams.Add(team); db.TeamMembers.Add(new() { TenantId = tenant.Id, TeamId = team.Id, UserId = actor.Id });
        var files = Enumerable.Range(0, 520).Select(i => new FileEntry { TenantId = tenant.Id, OwnerId = owner.Id, FolderId = child.Id, Name = $"{i}.txt", StorageKey = $"{i}", ContentType = "text/plain" }).ToArray(); var foreign = new FileEntry { TenantId = other.Id, OwnerId = foreignUser.Id, Name = "foreign", ContentType = "text/plain" }; db.FileEntries.AddRange(files); db.FileEntries.Add(foreign);
        foreach (var file in files.Append(foreign)) db.FileContentVersions.Add(new() { TenantId = file.TenantId, FileId = file.Id, ProviderId = provider.Id, ObjectKey = $"immutable/{file.Id}", Hash = "test", ContentType = "text/plain", ScanStatus = FileScanStatus.Clean });
        var now = DateTimeOffset.UtcNow; db.ResourceGrants.Add(new() { TenantId = tenant.Id, ResourceKind = ResourceKind.Folder, ResourceId = root.Id, PrincipalKind = PrincipalKind.Team, PrincipalId = team.Id, Role = ResourceRole.Viewer, ExpiresAt = now.AddHours(1) }); db.ResourceGrants.Add(new() { TenantId = tenant.Id, ResourceKind = ResourceKind.File, ResourceId = files[0].Id, PrincipalKind = PrincipalKind.User, PrincipalId = actor.Id, Role = ResourceRole.Editor, ExpiresAt = now.AddMinutes(1) }); await db.SaveChangesAsync();
        var counter = new Counter(); var options = new DbContextOptionsBuilder<AppDbContext>().UseMySql(db.Database.GetConnectionString()!, new MySqlServerVersion(new Version(8, 4, 0))).AddInterceptors(counter).Options;
        await using var scopedA = new AppDbContext(options, new DateTimeProvider(), new Current(actor.Id, Guid.NewGuid(), tenant.Id)); var query = new FileAclQuery(scopedA); var context = new FileAccessContext(new(actor.Id, Guid.NewGuid()), tenant.Id); var future = now.AddMinutes(2); var ids = files.Select(f => f.Id).Append(foreign.Id).ToArray();
        var rows = await query.ResolveManyAsync(context, ids, future, default); Assert.Equal(520, rows.Count); Assert.Equal(11, counter.Reads); Assert.All(rows[files[0].Id].Grants, g => Assert.False(g.CanWrite));
        Assert.Equal(520, await (await query.ReadScopeAsync(context, future, default)).CountAsync());
        await using var scopedB = new AppDbContext(options, new DateTimeProvider(), new Current(foreignUser.Id, Guid.NewGuid(), other.Id)); var b = new FileAclQuery(scopedB); var otherContext = new FileAccessContext(new(foreignUser.Id, Guid.NewGuid()), other.Id);
        Assert.Equal(new[] { foreign.Id }, (await b.ResolveManyAsync(otherContext, ids, future, default)).Keys.ToArray()); Assert.Equal(new[] { foreign.Id }, await (await b.ReadScopeAsync(otherContext, future, default)).Select(f => f.Id).ToArrayAsync());
    }
    [Fact]
    public async Task PendingReplaysAndRevocationAreFailClosedWithRealCoordinatorAndRedis()
    {
        await using var db = mysql.CreateDbContext(); var tenant = new Tenant(Guid.NewGuid(), "ACL mutation", Guid.NewGuid().ToString("N"), 10000); var owner = new User(tenant.Id, null, $"mutation-owner-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null); var viewer = new User(tenant.Id, null, $"mutation-viewer-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null); var session = new RefreshToken(Guid.NewGuid(), owner.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddHours(1), null); var viewerSession = new RefreshToken(Guid.NewGuid(), viewer.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddHours(1), null); var provider = new StorageProvider { Name = "ACL", ConfigEncrypted = "fixture", TotalCapacityBytes = 10000 }; var file = new FileEntry { TenantId = tenant.Id, OwnerId = owner.Id, Name = "test.txt", StorageKey = "test", ContentType = "text/plain" };
        db.Tenants.Add(tenant); db.Users.AddRange(owner, viewer); db.RefreshTokens.AddRange(session, viewerSession); db.StorageProviders.Add(provider); db.FileEntries.Add(file); db.FileContentVersions.Add(new() { TenantId = tenant.Id, FileId = file.Id, ProviderId = provider.Id, ObjectKey = "immutable/test", Hash = "test", ContentType = "text/plain", ScanStatus = FileScanStatus.Clean }); db.ResourceGrants.Add(new() { TenantId = tenant.Id, ResourceKind = ResourceKind.File, ResourceId = file.Id, PrincipalKind = PrincipalKind.User, PrincipalId = viewer.Id, Role = ResourceRole.Viewer });
        var roleId = Guid.NewGuid(); await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Roles (Id, Name, CreatedAt) VALUES ({roleId}, {"AclTest"}, {DateTime.UtcNow})");
        foreach (var name in new[] { "file.read", "file.share", "file.delete" }) { var p = await db.Permissions.SingleOrDefaultAsync(p => p.Name == name); if (p is null) { p = new(Guid.NewGuid(), name, null); db.Permissions.Add(p); } db.RolePermissions.Add(new(Guid.NewGuid(), roleId, p.Id)); }
        db.UserRoles.AddRange(new UserRole(Guid.NewGuid(), owner.Id, roleId), new UserRole(Guid.NewGuid(), viewer.Id, roleId)); await db.SaveChangesAsync();
        using var client = redis.CreateClient(); var readiness = new RedisReadiness(client); await new SecurityProjectionBootstrapper(db, client, readiness).RebuildAsync(default); var snapshots = new RedisAccessSnapshotStore(client, readiness); var evaluator = new AccessPolicyEvaluator(); var acl = new FileAclQuery(db); var access = new FileAccessService(snapshots, acl, evaluator); var resolver = new FileAccessContextResolver(new Current(owner.Id, session.Id, tenant.Id), db, snapshots); var coordinator = new AuthorizationMutationCoordinator(db, client, readiness, NullLogger<AuthorizationMutationCoordinator>.Instance); var mutations = new FileMutationService(db, resolver, snapshots, access, acl, evaluator, coordinator); var viewerContext = new FileAccessContext(new(viewer.Id, viewerSession.Id), tenant.Id);
        var original = await access.CheckManyAsync(viewerContext, new[] { file.Id }, default); Assert.Single(original);
        var command = new ManageAccessCommand(AccessMutationKind.UpsertFileGrant, file.Id, viewer.Id,
            PrincipalKind.User, ResourceRole.Editor, null, null, "upsert");
        var receipt = await mutations.ManageAsync(command, default);
        Assert.Equal(AuthorizationOperationStatus.Pending, receipt.Status);
        Assert.Equal(1, receipt.Version); // Bootstrap and absent DB scopes start at zero.
        await Assert.ThrowsAsync<SecurityUnavailableException>(() =>
            access.AssertStillValidAsync(viewerContext, original.Values.ToArray(), default));
        Assert.Equal(receipt, await mutations.ManageAsync(command, default));
        await Assert.ThrowsAsync<AuthorizationMutationConflictException>(() =>
            mutations.ManageAsync(command with { Role = ResourceRole.Owner }, default));
        await new AuthorizationProjectionWriter(db, client).PublishAsync(receipt.OperationId, default);
        await Assert.ThrowsAsync<AuthorizationChangedException>(() =>
            access.AssertStillValidAsync(viewerContext, original.Values.ToArray(), default));
        Assert.Equal(ResourceRole.Editor, (await db.ResourceGrants.SingleAsync(g => g.ResourceId == file.Id)).Role);
        var upgraded = await access.CheckManyAsync(viewerContext, new[] { file.Id }, default);
        Assert.Single(upgraded);
        var revoked = await mutations.ManageAsync(command with
        {
            Kind = AccessMutationKind.RevokeFileGrant, IdempotencyKey = "revoke"
        }, default);
        await new AuthorizationProjectionWriter(db, client).PublishAsync(revoked.OperationId, default);
        await Assert.ThrowsAsync<AuthorizationChangedException>(() =>
            access.AssertStillValidAsync(viewerContext, upgraded.Values.ToArray(), default));
        Assert.Empty(await access.CheckManyAsync(viewerContext, new[] { file.Id }, default));
        var deleted = await mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default); Assert.Equal(deleted, await mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default)); await new AuthorizationProjectionWriter(db, client).PublishAsync(deleted.OperationId, default);
        var purged = await mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, "purge", default); Assert.Equal(purged, await mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, "purge", default)); Assert.Equal(FileLifecycle.Purging, file.Lifecycle); Assert.NotNull(file.QuotaReleasedAt); Assert.Equal(1, await db.DocumentOutboxMessages.CountAsync(m => m.FileId == file.Id && m.EventType == "DocumentDeleted"));
        Assert.Equal("blocked", await client.ExecuteAsync("block-session", new[] { $"auth:session:{session.Id:N}" }, Array.Empty<string>(), default)); Assert.Null(session.RevokedAt); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, "purge", default));
        await new AuthorizationProjectionWriter(db, client).PublishAsync(purged.OperationId, default);
    }
}
