using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Infrastructure.Security;
using CloudDms.IntegrationTests.Support;
using Xunit;

namespace CloudDms.IntegrationTests.Authorization;

[Collection("Redis security readiness")]
public sealed class RedisAuthorizationTests(RedisFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CompleteSnapshotWithoutGrantDeniesAccess()
    {
        using var context = await CreateServiceAsync(seedGrant: false);

        var result = await context.Service.CheckAsync(
            context.Actor,
            PermissionNames.FileRead,
            context.Target,
            CancellationToken.None);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
        Assert.NotNull(result.Stamp);
    }

    [Fact]
    public async Task MissingSnapshotReturnsUnavailable()
    {
        using var context = await CreateServiceAsync(seedScope: false);

        var result = await context.Service.CheckAsync(
            context.Actor,
            PermissionNames.FileRead,
            context.Target,
            CancellationToken.None);

        Assert.Equal(AuthorizationDecision.Unavailable, result.Decision);
    }

    [Fact]
    public async Task UpdatingScopeReturnsUnavailable()
    {
        using var context = await CreateServiceAsync(scopeUpdating: true);

        var result = await context.Service.CheckAsync(
            context.Actor,
            PermissionNames.FileRead,
            context.Target,
            CancellationToken.None);

        Assert.Equal(AuthorizationDecision.Unavailable, result.Decision);
    }

    [Fact]
    public async Task ChangedVersionPreventsSideEffect()
    {
        using var context = await CreateServiceAsync(ownerIsActor: true);
        var result = await context.Service.CheckAsync(
            context.Actor,
            PermissionNames.FileRead,
            context.Target,
            CancellationToken.None);

        Assert.Equal(AuthorizationDecision.Allow, result.Decision);
        Assert.NotNull(result.Stamp);

        var updatedUser = context.UserSnapshot with { Version = context.UserSnapshot.Version + 1 };
        await fixture.SetStringAsync(
            context.UserKey,
            JsonSerializer.Serialize(updatedUser, JsonOptions));

        var sideEffectCalls = 0;
        await Assert.ThrowsAsync<AuthorizationChangedException>(
            async () =>
            {
                await context.Service.AssertStillValidAsync(context.Actor, result.Stamp!, CancellationToken.None);
                sideEffectCalls++;
            });
        Assert.Equal(0, sideEffectCalls);
    }

    [Fact]
    public async Task ExpiredGrantPreventsSideEffect()
    {
        using var context = await CreateServiceAsync(seedGrant: true);
        var result = await context.Service.CheckAsync(
            context.Actor,
            PermissionNames.FileRead,
            context.Target,
            CancellationToken.None);

        Assert.Equal(AuthorizationDecision.Allow, result.Decision);

        var grantKey = $"dms:grant:{context.Target.Id:N}:{context.Actor.UserId:N}";
        await fixture.SetStringAsync(
            grantKey,
            JsonSerializer.Serialize(
                new ShareGrantSeed(
                    context.Actor.UserId,
                    CanRead: true,
                    CanWrite: false,
                    CanDelete: false,
                    ExpiresAtUnixMilliseconds: DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds(),
                    ResourceVersion: 1,
                    Complete: true),
                JsonOptions));

        var sideEffectCalls = 0;
        await Assert.ThrowsAsync<AuthorizationChangedException>(
            async () =>
            {
                await context.Service.AssertStillValidAsync(context.Actor, result.Stamp!, CancellationToken.None);
                sideEffectCalls++;
            });
        Assert.Equal(0, sideEffectCalls);
    }

    [Fact]
    public async Task RedisUnavailableReturnsUnavailableWithoutFallback()
    {
        using var context = await CreateServiceAsync();
        await fixture.StopAsync();

        try
        {
            var result = await context.Service.CheckAsync(
                context.Actor,
                PermissionNames.FileRead,
                context.Target,
                CancellationToken.None);

            Assert.Equal(AuthorizationDecision.Unavailable, result.Decision);
        }
        finally
        {
            await fixture.StartAsync();
        }
    }

    [Fact]
    public async Task CancelRestoresPreviousScopeSnapshot()
    {
        using var client = fixture.CreateClient();
        var readiness = new RedisReadiness(client);
        var generation = Guid.NewGuid().ToString("N");
        var runId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
        await readiness.SetReadyAsync(generation, runId, CancellationToken.None);

        var operationId = Guid.NewGuid();
        const string scopeKey = "dms:scope:global";
        const string backupKey = "security:change-backup:test";
        var previous = JsonSerializer.Serialize(new { version = 1L, updating = false, complete = true }, JsonOptions);
        await fixture.SetStringAsync(scopeKey, previous);

        var beginResult = await client.ExecuteAsync(
            "begin-change",
            new[] { "security:meta", scopeKey, backupKey },
            new[] { generation, runId, operationId.ToString("N"), "2" },
            CancellationToken.None);
        Assert.Equal("started", beginResult);

        var cancelResult = await client.ExecuteAsync(
            "cancel-change",
            new[] { scopeKey, backupKey },
            new[] { operationId.ToString("N"), "2" },
            CancellationToken.None);

        Assert.Equal("cancelled", cancelResult);
        Assert.Equal(previous, await fixture.GetStringAsync(scopeKey));
    }

    [Fact]
    public async Task OldCancellationCannotClearNewerScopeMarker()
    {
        using var client = fixture.CreateClient();
        var scopeKey = "dms:scope:global";
        var newerOperationId = Guid.NewGuid();
        var newerMarker = JsonSerializer.Serialize(new
        {
            version = 3L,
            updating = true,
            complete = true,
            operationId = newerOperationId.ToString("N")
        }, JsonOptions);
        await fixture.SetStringAsync(scopeKey, newerMarker);

        var result = await client.ExecuteAsync(
            "cancel-change",
            new[] { scopeKey, "security:change-backup:old" },
            new[] { Guid.NewGuid().ToString("N"), "2" },
            CancellationToken.None);

        Assert.Equal("not-owner", result);
        Assert.Equal(newerMarker, await fixture.GetStringAsync(scopeKey));
    }

    [Fact]
    public async Task PublishRequiresCurrentOperationAndCompleteSnapshot()
    {
        using var client = fixture.CreateClient();
        var scopeKey = "dms:scope:global";
        var operationId = Guid.NewGuid();
        await fixture.SetStringAsync(
            scopeKey,
            JsonSerializer.Serialize(new
            {
                version = 2L,
                updating = true,
                complete = true,
                operationId = operationId.ToString("N")
            }, JsonOptions));

        var invalidSnapshotResult = await client.ExecuteAsync(
            "publish-change",
            new[] { scopeKey, "security:change-backup:test" },
            new[] { operationId.ToString("N"), "2", """{"version":2,"complete":false}""" },
            CancellationToken.None);
        Assert.Equal("invalid-snapshot", invalidSnapshotResult);

        var publishResult = await client.ExecuteAsync(
            "publish-change",
            new[] { scopeKey, "security:change-backup:test" },
            new[] { operationId.ToString("N"), "2", """{"version":2,"complete":true}""" },
            CancellationToken.None);

        Assert.Equal("published", publishResult);
        using var publishedSnapshot = JsonDocument.Parse((await fixture.GetStringAsync(scopeKey))!);
        Assert.Equal(2, publishedSnapshot.RootElement.GetProperty("version").GetInt32());
        Assert.True(publishedSnapshot.RootElement.GetProperty("complete").GetBoolean());
        Assert.False(publishedSnapshot.RootElement.GetProperty("updating").GetBoolean());
    }

    [Fact]
    public async Task FileWithoutDepartmentSnapshotCanBeAuthorizedByItsOwner()
    {
        using var context = await CreateServiceAsync(ownerIsActor: true, resourceHasDepartment: false);

        var result = await context.Service.CheckAsync(
            context.Actor,
            PermissionNames.FileRead,
            context.Target,
            CancellationToken.None);

        Assert.Equal(AuthorizationDecision.Allow, result.Decision);
    }

    private async Task<AuthorizationTestContext>
        CreateServiceAsync(
            bool seedScope = true,
            bool seedGrant = false,
            bool ownerIsActor = false,
            bool scopeUpdating = false,
            bool resourceHasDepartment = true)
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var generation = Guid.NewGuid().ToString("N");
        var actor = new ActorIdentity(userId, sessionId);
        var target = new AuthorizationTarget("file", fileId);
        var currentRunId = await GetCurrentRunIdAsync();
        await SetReadyAsync(generation, currentRunId);

        var user = new UserSeed(
            userId,
            tenantId,
            departmentId,
            true,
            1,
            new[] { "User" },
            new[] { PermissionNames.FileRead },
            true);
        var session = new
        {
            sessionId,
            userId,
            isActive = true,
            blocked = false,
            expiresAtUnixMilliseconds = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            version = 1L,
            complete = true
        };
        var file = new
        {
            id = fileId,
            tenantId,
            departmentId = resourceHasDepartment ? departmentId : (Guid?)null,
            ownerId = ownerIsActor ? userId : Guid.NewGuid(),
            lifecycle = "Active",
            version = 1L,
            complete = true
        };

        await fixture.SetStringAsync(
            $"dms:user:{userId:N}",
            JsonSerializer.Serialize(user, JsonOptions));
        await fixture.SetStringAsync(
            $"auth:session:{sessionId:N}",
            JsonSerializer.Serialize(session, JsonOptions));
        await fixture.SetStringAsync(
            $"dms:file:{fileId:N}",
            JsonSerializer.Serialize(file, JsonOptions));
        if (seedScope)
        {
            var scopeSnapshot = JsonSerializer.Serialize(
                new { version = 1L, updating = scopeUpdating, complete = true },
                JsonOptions);
            await fixture.SetStringAsync("dms:scope:global", scopeSnapshot);
            await fixture.SetStringAsync($"dms:scope:tenant:{tenantId:N}", scopeSnapshot);
            await fixture.SetStringAsync($"dms:scope:department:{departmentId:N}", scopeSnapshot);
        }

        if (seedGrant)
        {
            await fixture.SetStringAsync(
                $"dms:grant:{fileId:N}:{userId:N}",
                JsonSerializer.Serialize(new
                {
                    userId,
                    canRead = true,
                    canWrite = false,
                    canDelete = false,
                    expiresAtUnixMilliseconds = (long?)null,
                    resourceVersion = 1L,
                    complete = true
                }, JsonOptions));
        }

        var client = fixture.CreateClient();
        var readiness = new RedisReadiness(client);
        var store = new RedisAccessSnapshotStore(client, readiness);
        return new AuthorizationTestContext(
            client,
            actor,
            target,
            new AuthorizationService(store, new AccessPolicyEvaluator()),
            $"dms:user:{userId:N}",
            user);
    }

    private async Task<string> GetCurrentRunIdAsync()
    {
        using var client = fixture.CreateClient();
        return await new RedisReadiness(client).GetCurrentServerRunIdAsync(CancellationToken.None);
    }

    private async Task SetReadyAsync(string generation, string runId)
    {
        using var client = fixture.CreateClient();
        await new RedisReadiness(client).SetReadyAsync(generation, runId, CancellationToken.None);
    }

    private sealed record UserSeed(
        Guid UserId,
        Guid TenantId,
        Guid DepartmentId,
        bool IsActive,
        long Version,
        string[] Roles,
        string[] Permissions,
        bool Complete);

    private sealed record ShareGrantSeed(
        Guid UserId,
        bool CanRead,
        bool CanWrite,
        bool CanDelete,
        long? ExpiresAtUnixMilliseconds,
        long ResourceVersion,
        bool Complete);

    private sealed record AuthorizationTestContext(
        RedisSecurityClient RedisClient,
        ActorIdentity Actor,
        AuthorizationTarget Target,
        AuthorizationService Service,
        string UserKey,
        UserSeed UserSnapshot) : IDisposable
    {
        public void Dispose() => RedisClient.Dispose();
    }
}
