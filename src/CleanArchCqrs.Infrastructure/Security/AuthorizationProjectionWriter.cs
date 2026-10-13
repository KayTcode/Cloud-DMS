using System.Text.Json;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class AuthorizationProjectionWriter(
    AppDbContext dbContext,
    ISecurityRedis redis) : IAuthorizationProjectionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID is required.", nameof(operationId));
        }

        var operation = await dbContext.AuthorizationOperations
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken)
            ?? throw new InvalidOperationException($"Authorization operation '{operationId}' was not found.");

        if (operation.Status == AuthorizationOperationStatus.Applied)
        {
            return;
        }

        var outbox = await dbContext.OutboxMessages
            .SingleOrDefaultAsync(
                item => item.AggregateKey == operation.ScopeKey
                    && item.Version == operation.Version,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Authorization operation '{operationId}' has no matching outbox message.");

        var scopeKey = GetRedisScopeKey(operation.ScopeKey);
        var indexKey = $"security:scope-index:{operation.ScopeKey}";
        var expectedOperationId = operation.Id.ToString("N").ToLowerInvariant();
        var expectedVersion = operation.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var newProjections = await new AuthorizationProjectionBuilder(dbContext).BuildScopeProjectionsAsync(operation.ScopeKey, cancellationToken);
        var previousKeys = await ReadIndexedKeysAsync(indexKey, cancellationToken);
        var nextKeys = new HashSet<string>(newProjections.Select(projection => projection.Key), StringComparer.Ordinal);

        foreach (var projection in newProjections)
        {
            var result = await redis.ExecuteAsync(
                "set-scope-projection",
                new[] { scopeKey, projection.Key, indexKey },
                new[] { expectedOperationId, expectedVersion, operation.ScopeKey, projection.Json },
                cancellationToken);
            EnsureResult(result, "stored", operationId);
        }

        foreach (var staleKey in previousKeys.Where(key => !nextKeys.Contains(key)))
        {
            var result = await redis.ExecuteAsync(
                "remove-scope-projection",
                new[] { scopeKey, staleKey, indexKey },
                new[] { expectedOperationId, expectedVersion },
                cancellationToken);
            EnsureResult(result, "removed", operationId);
        }

        var publishResult = await redis.ExecuteAsync(
            "publish-change",
            new[] { scopeKey, $"security:change-backup:{operation.Id:N}" },
            new[]
            {
                expectedOperationId,
                expectedVersion,
                JsonSerializer.Serialize(
                    new { version = operation.Version, complete = true },
                    JsonOptions)
            },
            cancellationToken);

        if (publishResult is not ("published" or "already-published"))
        {
            throw new SecurityUnavailableException();
        }

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            operation.Status = AuthorizationOperationStatus.Applied;
            outbox.PublishedAt ??= DateTimeOffset.UtcNow;

            var scope = await dbContext.AuthorizationScopes
                .SingleOrDefaultAsync(item => item.ScopeKey == operation.ScopeKey, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Authorization scope '{operation.ScopeKey}' was not found.");
            if (scope.ActiveOperationId == operation.Id && scope.Version == operation.Version)
            {
                scope.IsUpdating = false;
                scope.ActiveOperationId = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task<IReadOnlyList<string>> ReadIndexedKeysAsync(
        string indexKey,
        CancellationToken cancellationToken)
    {
        var result = await redis.ExecuteAsync(
            "list-scope-projections",
            new[] { indexKey },
            Array.Empty<string>(),
            cancellationToken);
        return string.IsNullOrEmpty(result)
            ? Array.Empty<string>()
            : result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetRedisScopeKey(string scopeKey)
    {
        if (scopeKey == "global")
        {
            return "dms:scope:global";
        }

        var separator = scopeKey.IndexOf(':');
        var kind = scopeKey[..separator];
        var id = Guid.Parse(scopeKey[(separator + 1)..]);
        return $"dms:scope:{kind}:{id:N}".ToLowerInvariant();
    }

    private static void EnsureResult(string actual, string expected, Guid operationId)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new SecurityUnavailableException(
                new InvalidOperationException(
                    $"Projection update for authorization operation '{operationId}' was rejected."));
        }
    }

}
