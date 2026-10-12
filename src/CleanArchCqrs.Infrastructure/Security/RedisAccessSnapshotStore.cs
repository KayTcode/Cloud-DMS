using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class RedisAccessSnapshotStore(
    ISecurityRedis redis,
    ISecurityReadiness readiness) : IAccessSnapshotStore
{
    private const string ReadinessKey = "security:meta";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<AccessEvaluationInput> ReadAsync(
        ActorIdentity actor,
        AuthorizationTarget target,
        CancellationToken cancellationToken)
    {
        ValidateActorAndTarget(actor, target);

        var serverRunId = await readiness.GetCurrentServerRunIdAsync(cancellationToken);
        var generation = await readiness.AssertReadyAsync(cancellationToken);
        var targetId = target.Id?.ToString("N").ToLowerInvariant() ?? string.Empty;
        var targetKey = target.Id.HasValue
            ? $"dms:{target.Kind}:{targetId}"
            : "dms:target:none";
        var grantKey = target.Kind == "file" && target.Id.HasValue
            ? $"dms:grant:{targetId}:{actor.UserId:N}".ToLowerInvariant()
            : "dms:grant:none:none";

        var snapshotJson = await redis.ExecuteAsync(
            "evaluate-access",
            new[]
            {
                ReadinessKey,
                $"dms:user:{actor.UserId:N}".ToLowerInvariant(),
                $"auth:session:{actor.SessionId:N}".ToLowerInvariant(),
                targetKey,
                grantKey
            },
            new[]
            {
                generation,
                serverRunId,
                target.Kind,
                targetId,
                actor.UserId.ToString("N").ToLowerInvariant(),
                actor.SessionId.ToString("N").ToLowerInvariant()
            },
            cancellationToken);

        var verifiedRunId = await readiness.GetCurrentServerRunIdAsync(cancellationToken);
        if (!string.Equals(serverRunId, verifiedRunId, StringComparison.Ordinal))
        {
            throw new SecurityUnavailableException();
        }

        var envelope = Deserialize<SnapshotEnvelope>(snapshotJson);
        if (envelope is null
            || !envelope.SecurityReady
            || string.IsNullOrWhiteSpace(envelope.Generation))
        {
            throw new SecurityUnavailableException();
        }

        var user = Deserialize<UserSnapshotWire>(envelope.UserJson);
        var session = Deserialize<SessionSnapshotWire>(envelope.SessionJson);
        var resource = Deserialize<ResourceSnapshotWire>(envelope.ResourceJson);
        var grant = Deserialize<ShareGrantWire>(envelope.GrantJson);
        var scopes = Deserialize<ScopeSnapshotWire[]>(envelope.ScopesJson) ?? Array.Empty<ScopeSnapshotWire>();
        var complete = envelope.SnapshotsComplete
            && user is { Complete: true }
            && session is { Complete: true }
            && (string.IsNullOrEmpty(targetId) || resource is { Complete: true })
            && scopes.Length > 0
            && scopes.All(scope => scope.Complete);

        var userSnapshot = user is null
            ? null
            : new UserAccessSnapshot(
                user.UserId,
                user.TenantId,
                user.DepartmentId,
                user.IsActive,
                user.Version,
                new HashSet<string>(user.Roles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(user.Permissions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase));
        var sessionSnapshot = session is null
            ? null
            : new SessionAccessSnapshot(
                session.SessionId,
                session.UserId,
                session.IsActive && !session.Blocked,
                FromUnixTimeMilliseconds(session.ExpiresAtUnixMilliseconds),
                session.Version,
                session.Complete);
        var resourceSnapshot = resource is null
            ? null
            : new ResourceAccessSnapshot(
                resource.Id,
                resource.TenantId,
                resource.DepartmentId,
                resource.OwnerId,
                resource.Lifecycle,
                resource.Version);
        var grants = grant is null
            ? Array.Empty<ShareGrant>()
            : new[]
            {
                new ShareGrant(
                    grant.UserId,
                    grant.CanRead,
                    grant.CanWrite,
                    grant.CanDelete,
                    grant.ExpiresAtUnixMilliseconds.HasValue
                        ? FromUnixTimeMilliseconds(grant.ExpiresAtUnixMilliseconds.Value)
                        : null)
            };
        var scopeSnapshots = scopes
            .Select(scope => new ScopeAccessSnapshot(
                scope.Key,
                scope.Version,
                scope.IsUpdating,
                scope.Complete))
            .ToArray();

        return new AccessEvaluationInput(
            userSnapshot,
            sessionSnapshot,
            resourceSnapshot,
            grants,
            scopeSnapshots,
            complete,
            FromUnixTimeMilliseconds(envelope.ServerTimeUnixMilliseconds),
            envelope.Generation,
            target);
    }

    private static void ValidateActorAndTarget(ActorIdentity actor, AuthorizationTarget target)
    {
        if (actor.UserId == Guid.Empty || actor.SessionId == Guid.Empty)
        {
            throw new ArgumentException("A user and session ID are required.", nameof(actor));
        }

        if (target is null || target.Kind is not ("file" or "user" or "department" or "tenant" or "none"))
        {
            throw new ArgumentException("The authorization target kind is not supported.", nameof(target));
        }

        if (target.Id == Guid.Empty)
        {
            throw new ArgumentException("A target ID cannot be empty.", nameof(target));
        }
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SecurityUnavailableException(exception);
        }
    }

    private static DateTimeOffset FromUnixTimeMilliseconds(long milliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new SecurityUnavailableException(exception);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record SnapshotEnvelope(
        bool SecurityReady,
        bool SnapshotsComplete,
        long ServerTimeUnixMilliseconds,
        string Generation,
        string? UserJson,
        string? SessionJson,
        string? ResourceJson,
        string? GrantJson,
        string? ScopesJson);

    private sealed record UserSnapshotWire(
        Guid UserId,
        Guid? TenantId,
        Guid? DepartmentId,
        bool IsActive,
        long Version,
        string[]? Roles,
        string[]? Permissions,
        bool Complete);

    private sealed record SessionSnapshotWire(
        Guid SessionId,
        Guid UserId,
        bool IsActive,
        bool Blocked,
        long ExpiresAtUnixMilliseconds,
        long Version,
        bool Complete);

    private sealed record ResourceSnapshotWire(
        Guid Id,
        Guid? TenantId,
        Guid? DepartmentId,
        Guid OwnerId,
        CleanArchCqrs.Domain.Enums.FileLifecycle Lifecycle,
        long Version,
        bool Complete);

    private sealed record ShareGrantWire(
        Guid UserId,
        bool CanRead,
        bool CanWrite,
        bool CanDelete,
        long? ExpiresAtUnixMilliseconds,
        long ResourceVersion,
        bool Complete);

    private sealed record ScopeSnapshotWire(
        string Key,
        long Version,
        bool IsUpdating,
        bool Complete);
}
