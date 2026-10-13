using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Features.SystemAdmin.Commands.ChangeUserStatus;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Security;
using CloudDms.IntegrationTests.Support;
using CleanArchCqrs.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CloudDms.IntegrationTests.Authorization;

[CollectionDefinition("Authorization mutation coordinator", DisableParallelization = true)]
public sealed class AuthorizationMutationCollection :
    ICollectionFixture<MySqlFixture>,
    ICollectionFixture<RedisFixture>
{
}

[Collection("Authorization mutation coordinator")]
public sealed class AuthorizationMutationCoordinatorTests(
    MySqlFixture mysqlFixture,
    RedisFixture redisFixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RejectedMutationRollsBackDatabaseAndRestoresRedisScope()
    {
        using var client = redisFixture.CreateClient();
        var readiness = new RedisReadiness(client);
        var generation = Guid.NewGuid().ToString("N");
        var runId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
        await readiness.SetReadyAsync(generation, runId, CancellationToken.None);

        var scopeId = Guid.NewGuid();
        var scopeKey = $"tenant:{scopeId:N}";
        var redisScopeKey = $"dms:scope:tenant:{scopeId:N}";
        var previousSnapshot = JsonSerializer.Serialize(
            new { version = 0L, updating = false, complete = true },
            JsonOptions);
        await redisFixture.SetStringAsync(redisScopeKey, previousSnapshot);

        await using var dbContext = mysqlFixture.CreateDbContext(enableRetryOnFailure: true);
        var coordinator = new AuthorizationMutationCoordinator(
            dbContext,
            client,
            readiness,
            NullLogger<AuthorizationMutationCoordinator>.Instance);
        var request = new MutationRequest(scopeKey, Guid.NewGuid().ToString("N"), new string('c', 64));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteAsync(
                request,
                Guid.NewGuid(),
                _ => throw new InvalidOperationException("Command rejected."),
                CancellationToken.None));

        Assert.Null(await dbContext.AuthorizationScopes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey));
        Assert.Null(await dbContext.AuthorizationOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey));
        Assert.Equal(previousSnapshot, await redisFixture.GetStringAsync(redisScopeKey));
    }

    [Fact]
    public async Task MutationPersistsPendingReceiptAndEnforcesIdempotency()
    {
        using var client = redisFixture.CreateClient();
        var readiness = new RedisReadiness(client);
        var generation = Guid.NewGuid().ToString("N");
        var runId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
        await readiness.SetReadyAsync(generation, runId, CancellationToken.None);

        var scopeId = MySqlFixture.LegacyTenantId;
        var scopeKey = $"tenant:{scopeId:N}";
        var redisScopeKey = $"dms:scope:tenant:{scopeId:N}";
        var user = new User(
            MySqlFixture.LegacyTenantId,
            null,
            $"mutation-{Guid.NewGuid():N}@example.test",
            "test-hash",
            "Mutation",
            "Test",
            null);
        await using var dbContext = mysqlFixture.CreateDbContext(enableRetryOnFailure: true);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        await redisFixture.SetStringAsync(
            redisScopeKey,
            JsonSerializer.Serialize(new { version = 0L, updating = false, complete = true }, JsonOptions));

        var coordinator = new AuthorizationMutationCoordinator(
            dbContext,
            client,
            readiness,
            NullLogger<AuthorizationMutationCoordinator>.Instance);
        var request = new MutationRequest(scopeKey, Guid.NewGuid().ToString("N"), new string('a', 64));
        var persistCalls = 0;

        var receipt = await coordinator.ExecuteAsync(
            request,
            user.Id,
            token =>
            {
                persistCalls++;
                return ApplyStatusChangeAsync(dbContext, user.Id, token);
            },
            CancellationToken.None);

        Assert.Equal(AuthorizationOperationStatus.Pending, receipt.Status);
        Assert.Equal(1, persistCalls);
        Assert.Equal(receipt, await coordinator.GetAsync(receipt.OperationId, user.Id, CancellationToken.None));
        Assert.Null(await coordinator.GetAsync(receipt.OperationId, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<AuthorizationMutationConflictException>(
            () => coordinator.ExecuteAsync(
                request,
                Guid.NewGuid(),
                _ => Task.CompletedTask,
                CancellationToken.None));
        var duplicate = await coordinator.ExecuteAsync(
            request,
            user.Id,
            token =>
            {
                persistCalls++;
                return ApplyStatusChangeAsync(dbContext, user.Id, token);
            },
            CancellationToken.None);
        Assert.Equal(receipt, duplicate);
        Assert.Equal(1, persistCalls);

        await Assert.ThrowsAsync<AuthorizationMutationConflictException>(
            () => coordinator.ExecuteAsync(
                request with { PayloadHash = new string('b', 64) },
                user.Id,
                _ => Task.CompletedTask,
                CancellationToken.None));

        var operation = await dbContext.AuthorizationOperations.FindAsync(receipt.OperationId);
        var scope = await dbContext.AuthorizationScopes.SingleAsync(item => item.ScopeKey == scopeKey);
        var outboxCount = await dbContext.OutboxMessages.CountAsync(
            item => item.AggregateKey == scopeKey && item.Version == receipt.Version);
        Assert.NotNull(operation);
        Assert.True(scope.IsUpdating);
        Assert.Equal(receipt.OperationId, scope.ActiveOperationId);
        Assert.Equal(1, outboxCount);

        using var marker = JsonDocument.Parse((await redisFixture.GetStringAsync(redisScopeKey))!);
        Assert.True(marker.RootElement.GetProperty("updating").GetBoolean());
        Assert.Equal(receipt.OperationId.ToString("N"), marker.RootElement.GetProperty("operationId").GetString());

        await redisFixture.StopAsync();
        try
        {
            await Assert.ThrowsAsync<SecurityUnavailableException>(
                () => new AuthorizationProjectionWriter(dbContext, client)
                    .PublishAsync(receipt.OperationId, CancellationToken.None));

            var stillPending = await dbContext.AuthorizationOperations
                .AsNoTracking()
                .SingleAsync(item => item.Id == receipt.OperationId);
            Assert.Equal(AuthorizationOperationStatus.Pending, stillPending.Status);
        }
        finally
        {
            await redisFixture.StartAsync();
        }

        using var recoveryClient = redisFixture.CreateClient();
        await new AuthorizationProjectionWriter(dbContext, recoveryClient)
            .PublishAsync(receipt.OperationId, CancellationToken.None);

        var applied = await dbContext.AuthorizationOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == receipt.OperationId);
        var finishedScope = await dbContext.AuthorizationScopes
            .AsNoTracking()
            .SingleAsync(item => item.ScopeKey == scopeKey);
        var publishedOutbox = await dbContext.OutboxMessages
            .AsNoTracking()
            .SingleAsync(item => item.AggregateKey == scopeKey && item.Version == receipt.Version);
        Assert.Equal(AuthorizationOperationStatus.Applied, applied.Status);
        Assert.False(finishedScope.IsUpdating);
        Assert.Null(finishedScope.ActiveOperationId);
        Assert.NotNull(publishedOutbox.PublishedAt);
        using var projectedUser = JsonDocument.Parse((await redisFixture.GetStringAsync($"dms:user:{user.Id:N}"))!);
        Assert.Equal(user.Id, projectedUser.RootElement.GetProperty("userId").GetGuid());
        Assert.False(projectedUser.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, projectedUser.RootElement.GetProperty("version").GetInt32());
        Assert.True(projectedUser.RootElement.GetProperty("complete").GetBoolean());
        using var publishedScope = JsonDocument.Parse((await redisFixture.GetStringAsync(redisScopeKey))!);
        Assert.False(publishedScope.RootElement.GetProperty("updating").GetBoolean());

        await new AuthorizationProjectionWriter(dbContext, recoveryClient)
            .PublishAsync(receipt.OperationId, CancellationToken.None);
        var duplicatePublished = await dbContext.AuthorizationOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == receipt.OperationId);
        Assert.Equal(AuthorizationOperationStatus.Applied, duplicatePublished.Status);
        var appliedRetry = await coordinator.ExecuteAsync(
            request,
            user.Id,
            _ =>
            {
                persistCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.Equal(AuthorizationOperationStatus.Applied, appliedRetry.Status);
        Assert.Equal(1, persistCalls);
    }

    private static async Task ApplyStatusChangeAsync(
        CleanArchCqrs.Infrastructure.Persistence.AppDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await new ChangeUserStatusCommandHandler(dbContext)
            .Handle(new ChangeUserStatusCommand(userId, false), cancellationToken);
        Assert.True(result.IsSuccess);
    }
}
