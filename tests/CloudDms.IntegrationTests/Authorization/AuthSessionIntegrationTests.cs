using System.IdentityModel.Tokens.Jwt;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.Commands.RefreshToken;
using CleanArchCqrs.Application.Users.Commands.Login;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Infrastructure.Authentication;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Services;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CloudDms.IntegrationTests.Authorization;

[CollectionDefinition("Auth sessions", DisableParallelization = true)]
public sealed class AuthSessionsCollection : ICollectionFixture<MySqlFixture>, ICollectionFixture<RedisFixture> { }

[Collection("Auth sessions")]
public sealed class AuthSessionIntegrationTests(MySqlFixture mysql, RedisFixture redis)
{
    private static JwtTokenService Jwt() => new(Options.Create(new JwtOptions
    { SecretKey = new string('x', 64), Issuer = "test", Audience = "test", ExpirationMinutes = 15 }));

    [Fact]
    public async Task LostBootstrapOwnerCannotPublishReadiness()
    {
        using var client = redis.CreateClient();
        var owner = Guid.NewGuid().ToString("N");
        var successor = Guid.NewGuid().ToString("N");
        Assert.Equal("started", await client.ExecuteAsync("begin-bootstrap", new[] { "security:meta", "security:bootstrap-lock" }, new[] { owner }, CancellationToken.None));
        await redis.SetStringAsync("security:bootstrap-lock", successor);
        try
        {
            Assert.Equal("not-owner", await client.ExecuteAsync("finish-bootstrap", new[] { "security:meta", "security:bootstrap-lock" },
                new[] { owner, "generation", "run", "1", "[]", "[]" }, CancellationToken.None));
            await Assert.ThrowsAsync<SecurityUnavailableException>(() => new RedisReadiness(client).AssertReadyAsync(CancellationToken.None));
        }
        finally { await client.ExecuteAsync("abort-bootstrap", new[] { "security:bootstrap-lock" }, new[] { successor }, CancellationToken.None); }
    }

    [Fact]
    public async Task RedisFailurePreventsAccessTokenIssuance()
    {
        await using var db = mysql.CreateDbContext();
        var user = new User(null, null, $"redis-failure-{Guid.NewGuid():N}@example.test", "hash", "Test", "Session", null) { EmailConfirmed = true };
        var session = new RefreshToken(Guid.NewGuid(), user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(7), null);
        db.Users.Add(user); db.RefreshTokens.Add(session); await db.SaveChangesAsync();
        using var client = redis.CreateClient();
        var service = new AuthSessionService(db, client, new RedisReadiness(client), Jwt());
        await redis.StopAsync();
        try { await Assert.ThrowsAsync<SecurityUnavailableException>(() => service.IssueAsync(user, "Employee", Array.Empty<string>(), session.Id, CancellationToken.None)); }
        finally { await redis.StartAsync(); }
    }

    [Fact]
    public async Task ConcurrentRefreshHasOneDatabaseWinnerAndOldSessionStaysInactive()
    {
        const string raw = "session-concurrent-refresh-test";
        Guid oldId;
        Guid userId;
        await using (var setup = mysql.CreateDbContext())
        {
            var user = new User(null, null, $"concurrent-{Guid.NewGuid():N}@example.test", "hash", "Test", "Session", null) { EmailConfirmed = true };
            userId = user.Id;
            oldId = Guid.NewGuid();
            setup.Users.Add(user);
            setup.RefreshTokens.Add(new RefreshToken(oldId, user.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), DateTime.UtcNow.AddDays(7), null));
            await setup.SaveChangesAsync();
        }
        using var client = redis.CreateClient();
        var readiness = new RedisReadiness(client);
        await using var firstDb = mysql.CreateDbContext();
        await using var secondDb = mysql.CreateDbContext();
        var gate = new AsyncPairGate();
        var firstSessions = new PairedSessions(new AuthSessionService(firstDb, client, readiness, Jwt()), gate);
        var secondSessions = new PairedSessions(new AuthSessionService(secondDb, client, readiness, Jwt()), gate);
        var auth = new EmptyAuthorizationRepository();
        var first = new RefreshTokenCommandHandler(firstDb, auth, Jwt(), firstSessions);
        var second = new RefreshTokenCommandHandler(secondDb, auth, Jwt(), secondSessions);
        var results = await Task.WhenAll(first.Handle(new(raw), CancellationToken.None), second.Handle(new(raw), CancellationToken.None));
        Assert.Single(results, x => x.IsSuccess);
        await using var verify = mysql.CreateDbContext();
        Assert.NotNull((await verify.RefreshTokens.SingleAsync(x => x.Id == oldId)).RevokedAt);
        Assert.Single(await verify.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync());
        await new AuthSessionService(verify, client, readiness, Jwt()).RebuildAsync(CancellationToken.None);
        var snapshot = await new RedisAccessSnapshotStore(client, readiness).ReadAsync(new(userId, oldId), new("none", null), CancellationToken.None);
        Assert.False(snapshot.Session!.IsActive);
    }

    [Fact]
    public async Task PendingDatabaseScopeReconstructsBarrierAndOutboxCanFinish()
    {
        await using var db = mysql.CreateDbContext();
        var tenant = new Tenant(Guid.NewGuid(), "Pending session test", Guid.NewGuid().ToString("N"), 1000);
        db.Tenants.Add(tenant); await db.SaveChangesAsync();
        using var client = redis.CreateClient();
        var readiness = new RedisReadiness(client);
        var sessions = new AuthSessionService(db, client, readiness, Jwt());
        await sessions.RebuildAsync(CancellationToken.None);
        var scope = $"tenant:{tenant.Id:N}";
        var coordinator = new AuthorizationMutationCoordinator(db, client, readiness, NullLogger<AuthorizationMutationCoordinator>.Instance);
        var receipt = await coordinator.ExecuteAsync(new MutationRequest(scope, Guid.NewGuid().ToString("N"), new string('a', 64)), MySqlFixture.LegacyUserId, _ => Task.CompletedTask, CancellationToken.None);
        await redis.RestartAsync();
        using var restoredClient = redis.CreateClient();
        await new AuthSessionService(db, restoredClient, new RedisReadiness(restoredClient), Jwt()).RebuildAsync(CancellationToken.None);
        var marker = await redis.GetStringAsync($"dms:scope:{scope}");
        Assert.Contains(receipt.OperationId.ToString("N"), marker);
        Assert.Contains("\"updating\":true", marker);
        await new AuthorizationProjectionWriter(db, restoredClient).PublishAsync(receipt.OperationId, CancellationToken.None);
        Assert.Equal(AuthorizationOperationStatus.Applied, (await db.AuthorizationOperations.SingleAsync(x => x.Id == receipt.OperationId)).Status);
        Assert.Contains("\"updating\":false", await redis.GetStringAsync($"dms:scope:{scope}"));
    }

    private sealed class EmptyAuthorizationRepository : IUserAuthorizationRepository
    {
        public Task<string?> GetRoleAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Employee");
        public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
    private sealed class AsyncPairGate
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            return ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
    private sealed class PairedSessions(IAuthSessionService inner, AsyncPairGate gate) : IAuthSessionService
    {
        public Task<JwtTokenResult> IssueAsync(User user, string role, IEnumerable<string> permissions, Guid sessionId, CancellationToken cancellationToken) => inner.IssueAsync(user, role, permissions, sessionId, cancellationToken);
        public async Task BlockAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            await inner.BlockAsync(sessionId, cancellationToken);
            await gate.WaitAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task RebuildCannotEraseRedisBarrierMissingFromDatabase()
    {
        using var client = redis.CreateClient();
        await using var db = mysql.CreateDbContext();
        var readiness = new RedisReadiness(client);
        var key = $"dms:scope:tenant:{Guid.NewGuid():N}";
        await redis.SetStringAsync(key, "{\"version\":1,\"updating\":true,\"complete\":true,\"operationId\":\"pending\"}");
        try
        {
            await Assert.ThrowsAsync<SecurityUnavailableException>(() => new AuthSessionService(db, client, readiness, Jwt()).RebuildAsync(CancellationToken.None));
            Assert.Contains("pending", await redis.GetStringAsync(key));
            await Assert.ThrowsAsync<SecurityUnavailableException>(() => readiness.AssertReadyAsync(CancellationToken.None));
        }
        finally { await redis.SetStringAsync(key, "{\"version\":0,\"updating\":false,\"complete\":true}"); }
    }

    [Fact]
    public async Task IssuedJwtWorksWithEvaluatorAndRevocationSurvivesRebuild()
    {
        await using var db = mysql.CreateDbContext();
        var tenant = new Tenant(Guid.NewGuid(), "Session test", Guid.NewGuid().ToString("N"), 1000);
        var user = new User(tenant.Id, null, $"session-{Guid.NewGuid():N}@example.test", new PasswordHasher().Hash("integration-password"), "Test", "Session", null) { EmailConfirmed = true };
        var file = new FileEntry { Id = Guid.NewGuid(), TenantId = tenant.Id, OwnerId = user.Id, Name = "test.txt", StorageKey = "test", ContentType = "text/plain" };
        db.Tenants.Add(tenant); db.Users.Add(user); db.FileEntries.Add(file);
        var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Name == PermissionNames.FileRead);
        if (permission is null) { permission = new Permission(Guid.NewGuid(), PermissionNames.FileRead, null); db.Permissions.Add(permission); }
        var role = new Role(Guid.NewGuid(), "SessionTester", null, tenant.Id);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Roles (Id, Name, CreatedAt) VALUES ({role.Id}, {role.Name}, {DateTime.UtcNow})");
        db.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, role.Id));
        db.RolePermissions.Add(new RolePermission(Guid.NewGuid(), role.Id, permission.Id));
        await db.SaveChangesAsync();
        using var client = redis.CreateClient();
        var readiness = new RedisReadiness(client);
        var service = new AuthSessionService(db, client, readiness, Jwt());
        var token = await new LoginCommandHandler(new UserRepository(db), new PasswordHasher(), Jwt(), new UserAuthorizationRepository(db), db, service)
            .Handle(new LoginCommand(user.Email, "integration-password"), CancellationToken.None);
        var sessionId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken).Claims.Single(x => x.Type == "sid").Value);
        var session = await db.RefreshTokens.SingleAsync(x => x.Id == sessionId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.RefreshToken))), session.TokenHash);
        Assert.Equal(session.Id.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken).Claims.Single(x => x.Type == "sid").Value);
        var store = new RedisAccessSnapshotStore(client, readiness);
        var evaluator = new AccessPolicyEvaluator();
        var actor = new ActorIdentity(user.Id, session.Id);
        var input = await store.ReadAsync(actor, new("file", file.Id), CancellationToken.None);
        Assert.Equal(AuthorizationDecision.Allow, evaluator.Evaluate(input, PermissionNames.FileRead).Decision);
        var index = await client.ExecuteAsync("list-scope-projections", new[] { $"security:scope-index:tenant:{tenant.Id:N}" }, Array.Empty<string>(), CancellationToken.None);
        Assert.Contains($"dms:file:{file.Id:N}", index);
        await service.BlockAsync(session.Id, CancellationToken.None);
        session.Revoke(); await db.SaveChangesAsync();
        input = await store.ReadAsync(actor, new("file", file.Id), CancellationToken.None);
        Assert.NotEqual(AuthorizationDecision.Allow, evaluator.Evaluate(input, PermissionNames.FileRead).Decision);
        await redis.RestartAsync();
        await Assert.ThrowsAsync<SecurityUnavailableException>(() => readiness.AssertReadyAsync(CancellationToken.None));
        using var restoredClient = redis.CreateClient();
        var restoredReadiness = new RedisReadiness(restoredClient);
        await new AuthSessionService(db, restoredClient, restoredReadiness, Jwt()).RebuildAsync(CancellationToken.None);
        input = await new RedisAccessSnapshotStore(restoredClient, restoredReadiness).ReadAsync(actor, new("file", file.Id), CancellationToken.None);
        Assert.NotEqual(AuthorizationDecision.Allow, evaluator.Evaluate(input, PermissionNames.FileRead).Decision);
    }
}
