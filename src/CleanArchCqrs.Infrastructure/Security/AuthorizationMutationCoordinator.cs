using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class AuthorizationMutationCoordinator(
    AppDbContext dbContext,
    ISecurityRedis redis,
    ISecurityReadiness readiness,
    ILogger<AuthorizationMutationCoordinator> logger) : IAuthorizationMutationCoordinator
{
    private const string ReadinessKey = "security:meta";

    public async Task<MutationReceipt> ExecuteAsync(
        MutationRequest request,
        Guid initiatedByUserId,
        Func<CancellationToken, Task> persistChange,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, persistChange);
        if (initiatedByUserId == Guid.Empty)
        {
            throw new ArgumentException("An initiating user ID is required.", nameof(initiatedByUserId));
        }
        request = request with
        {
            ScopeKey = NormalizeScopeKey(request.ScopeKey),
            PayloadHash = request.PayloadHash.ToLowerInvariant()
        };
        // Retrying this transaction could replay the caller's persistence callback or Redis marker.
        var strategy = new SingleAttemptExecutionStrategy(
            dbContext.GetService<ExecutionStrategyDependencies>());

        return await strategy.ExecuteAsync(
            () => ExecuteOnceAsync(request, initiatedByUserId, persistChange, cancellationToken));
    }

    private async Task<MutationReceipt> ExecuteOnceAsync(
        MutationRequest request,
        Guid initiatedByUserId,
        Func<CancellationToken, Task> persistChange,
        CancellationToken cancellationToken)
    {
        var redisScopeKey = GetRedisScopeKey(request.ScopeKey);
        var advisoryLockName = CreateAdvisoryLockName(request.ScopeKey);
        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var lockAcquired = false;
        try
        {
            lockAcquired = await AcquireScopeLockAsync(connection, advisoryLockName, cancellationToken);
            if (!lockAcquired)
            {
                throw new AuthorizationMutationPendingException();
            }

            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var existing = await dbContext.AuthorizationOperations
                .SingleOrDefaultAsync(
                    operation => operation.ScopeKey == request.ScopeKey
                        && operation.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                if (existing.InitiatedByUserId != initiatedByUserId
                    || !string.Equals(existing.PayloadHash, request.PayloadHash, StringComparison.Ordinal))
                {
                    throw new AuthorizationMutationConflictException();
                }

                return ToReceipt(existing);
            }

            var scope = await dbContext.AuthorizationScopes
                .SingleOrDefaultAsync(item => item.ScopeKey == request.ScopeKey, cancellationToken);
            if (scope is null)
            {
                scope = new AuthorizationScope
                {
                    ScopeKey = request.ScopeKey,
                    Version = 0
                };
                dbContext.AuthorizationScopes.Add(scope);
            }

            var version = checked(scope.Version + 1);
            var operation = new AuthorizationOperation
            {
                InitiatedByUserId = initiatedByUserId,
                ScopeKey = request.ScopeKey,
                IdempotencyKey = request.IdempotencyKey,
                PayloadHash = request.PayloadHash,
                Version = version,
                Status = AuthorizationOperationStatus.Pending
            };

            var serverRunId = await readiness.GetCurrentServerRunIdAsync(cancellationToken);
            var generation = await readiness.AssertReadyAsync(cancellationToken);
            var beginResult = await redis.ExecuteAsync(
                "begin-change",
                new[]
                {
                    ReadinessKey,
                    redisScopeKey,
                    $"security:change-backup:{operation.Id:N}"
                },
                new[]
                {
                    generation,
                    serverRunId,
                    operation.Id.ToString("N").ToLowerInvariant(),
                    version.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                cancellationToken);

            if (!string.Equals(beginResult, "started", StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                throw beginResult switch
                {
                    "busy" => new AuthorizationMutationPendingException(),
                    "not-ready" => new SecurityUnavailableException(),
                    "stale-version" => new SecurityUnavailableException(),
                    _ => new SecurityUnavailableException()
                };
            }

            var markerStarted = true;
            var commitAttempted = false;
            try
            {
                await persistChange(cancellationToken);

                scope.Version = version;
                scope.IsUpdating = true;
                scope.ActiveOperationId = operation.Id;
                dbContext.AuthorizationOperations.Add(operation);
                dbContext.OutboxMessages.Add(new OutboxMessage
                {
                    AggregateKey = request.ScopeKey,
                    Version = version,
                    Payload = JsonSerializer.Serialize(new OutboxPayload(
                        operation.Id,
                        request.ScopeKey,
                        version,
                        request.PayloadHash))
                });

                await dbContext.SaveChangesAsync(cancellationToken);
                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
                markerStarted = false;
                return ToReceipt(operation);
            }
            catch
            {
                if (!commitAttempted)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    if (markerStarted)
                    {
                        await CancelMarkerAsync(redisScopeKey, operation, CancellationToken.None);
                    }
                }
                else
                {
                    logger.LogError(
                        "Authorization mutation {OperationId} has an uncertain commit result; scope {ScopeKey} stays blocked.",
                        operation.Id,
                        request.ScopeKey);
                }

                throw;
            }
        }
        finally
        {
            if (lockAcquired)
            {
                await ReleaseScopeLockAsync(connection, advisoryLockName);
            }

            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task<MutationReceipt?> GetAsync(
        Guid operationId,
        Guid requesterUserId,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || requesterUserId == Guid.Empty)
        {
            throw new ArgumentException("An operation ID and requester ID are required.");
        }

        var operation = await dbContext.AuthorizationOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == operationId && item.InitiatedByUserId == requesterUserId,
                cancellationToken);

        return operation is null ? null : ToReceipt(operation);
    }

    private async Task<bool> AcquireScopeLockAsync(
        System.Data.Common.DbConnection connection,
        string lockName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@lockName, 5)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@lockName";
        parameter.Value = lockName;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    private static async Task ReleaseScopeLockAsync(
        System.Data.Common.DbConnection connection,
        string lockName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RELEASE_LOCK(@lockName)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@lockName";
        parameter.Value = lockName;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private async Task CancelMarkerAsync(
        string redisScopeKey,
        AuthorizationOperation operation,
        CancellationToken cancellationToken)
    {
        var result = await redis.ExecuteAsync(
            "cancel-change",
            new[]
            {
                redisScopeKey,
                $"security:change-backup:{operation.Id:N}"
            },
            new[]
            {
                operation.Id.ToString("N").ToLowerInvariant(),
                operation.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken);

        if (!string.Equals(result, "cancelled", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Authorization scope marker for operation {OperationId} could not be cleared and requires recovery.",
                operation.Id);
        }
    }

    private static string CreateAdvisoryLockName(string scopeKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scopeKey));
        return $"authz:{Convert.ToHexString(hash.AsSpan(0, 28)).ToLowerInvariant()}";
    }

    private static string GetRedisScopeKey(string scopeKey)
    {
        if (string.Equals(scopeKey, "global", StringComparison.Ordinal))
        {
            return "dms:scope:global";
        }

        var separator = scopeKey.IndexOf(':');
        if (separator <= 0
            || !Guid.TryParse(scopeKey.AsSpan(separator + 1), out var scopeId)
            || scopeId == Guid.Empty)
        {
            throw new ArgumentException("The authorization scope key is invalid.", nameof(scopeKey));
        }

        var prefix = scopeKey[..separator] switch
        {
            "tenant" => "tenant",
            "department" => "department",
            _ => throw new ArgumentException("The authorization scope kind is invalid.", nameof(scopeKey))
        };

        return $"dms:scope:{prefix}:{scopeId:N}".ToLowerInvariant();
    }

    private static string NormalizeScopeKey(string scopeKey)
    {
        if (string.Equals(scopeKey, "global", StringComparison.Ordinal))
        {
            return scopeKey;
        }

        var separator = scopeKey.IndexOf(':');
        if (separator <= 0
            || !Guid.TryParse(scopeKey.AsSpan(separator + 1), out var scopeId)
            || scopeId == Guid.Empty)
        {
            throw new ArgumentException("The authorization scope key is invalid.", nameof(scopeKey));
        }

        var kind = scopeKey[..separator] switch
        {
            "tenant" => "tenant",
            "department" => "department",
            _ => throw new ArgumentException("The authorization scope kind is invalid.", nameof(scopeKey))
        };

        return $"{kind}:{scopeId:N}".ToLowerInvariant();
    }

    private static void ValidateRequest(
        MutationRequest request,
        Func<CancellationToken, Task> persistChange)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(persistChange);

        if (request.ScopeKey.Length is 0 or > 255
            || request.IdempotencyKey.Length is 0 or > 128
            || request.PayloadHash.Length != 64
            || request.PayloadHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The mutation request is invalid.", nameof(request));
        }
    }

    private static MutationReceipt ToReceipt(AuthorizationOperation operation) =>
        new(operation.Id, operation.Version, operation.Status);

    private sealed record OutboxPayload(Guid OperationId, string ScopeKey, long Version, string PayloadHash);

    private sealed class SingleAttemptExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 0, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
