using System.Text.Json;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class RedisReadiness(RedisSecurityClient redisSecurityClient) : ISecurityReadiness
{
    private const string ReadinessKey = "security:meta";
    private const string SchemaVersion = "1";

    public Task<string> GetCurrentServerRunIdAsync(CancellationToken cancellationToken) =>
        redisSecurityClient.GetServerRunIdAsync(cancellationToken);

    public async Task<string> AssertReadyAsync(CancellationToken cancellationToken)
    {
        var firstRunId = await redisSecurityClient.GetServerRunIdAsync(cancellationToken);
        var metadataJson = await redisSecurityClient.ExecuteAsync(
            "get-readiness",
            new[] { ReadinessKey },
            Array.Empty<string>(),
            cancellationToken);
        var lastRunId = await redisSecurityClient.GetServerRunIdAsync(cancellationToken);

        if (!string.Equals(firstRunId, lastRunId, StringComparison.Ordinal))
        {
            throw new SecurityUnavailableException();
        }

        string?[]? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<string?[]>(metadataJson);
        }
        catch (JsonException exception)
        {
            throw new SecurityUnavailableException(exception);
        }

        if (metadata is not { Length: 4 })
        {
            throw new SecurityUnavailableException();
        }

        var generation = metadata[0];
        if (string.IsNullOrWhiteSpace(generation)
            || !string.Equals(metadata[1], "ready", StringComparison.Ordinal)
            || !string.Equals(metadata[2], firstRunId, StringComparison.Ordinal)
            || !string.Equals(metadata[3], SchemaVersion, StringComparison.Ordinal))
        {
            throw new SecurityUnavailableException();
        }

        return generation;
    }

    public async Task SetReadyAsync(
        string generation,
        string serverRunId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(generation) || string.IsNullOrWhiteSpace(serverRunId))
        {
            throw new ArgumentException("A generation and Redis server run ID are required.");
        }

        var currentRunId = await redisSecurityClient.GetServerRunIdAsync(cancellationToken);
        if (!string.Equals(currentRunId, serverRunId, StringComparison.Ordinal))
        {
            throw new SecurityUnavailableException();
        }

        await redisSecurityClient.ExecuteAsync(
            "set-readiness",
            new[] { ReadinessKey },
            new[] { generation, serverRunId, SchemaVersion },
            cancellationToken);

        var verifiedRunId = await redisSecurityClient.GetServerRunIdAsync(cancellationToken);
        if (!string.Equals(verifiedRunId, serverRunId, StringComparison.Ordinal))
        {
            throw new SecurityUnavailableException();
        }
    }
}
