using System.Data;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Infrastructure.Security;
public sealed class SecurityProjectionBootstrapper(AppDbContext db, ISecurityRedis redis, ISecurityReadiness readiness)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string LockKey = "security:bootstrap-lock";
    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        // A validated rollback can change a barrier while the DB snapshot is built.
        // Retry once with a new lease and transaction; repeated changes fail closed.
        for (var attempt = 0; attempt < 2; attempt++)
            if (await RebuildOnceAsync(cancellationToken)) return;
        throw new SecurityUnavailableException();
    }

    private async Task<bool> RebuildOnceAsync(CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid().ToString("N");
        var runId = await readiness.GetCurrentServerRunIdAsync(cancellationToken);
        if (await redis.ExecuteAsync("begin-bootstrap", new[] { "security:meta", LockKey }, new[] { owner }, cancellationToken) != "started")
            throw new SecurityUnavailableException();
        try
        {
            var published = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                var projections = (await new AuthorizationProjectionBuilder(db).BuildScopeProjectionsAsync("global", cancellationToken)).ToList();
                var scopes = await db.AuthorizationScopes.AsNoTracking().ToListAsync(cancellationToken);
                var keys = new HashSet<string>(StringComparer.Ordinal) { "global" };
                foreach (var tenant in await db.Tenants.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken)) keys.Add($"tenant:{tenant:N}");
                foreach (var department in await db.Departments.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken)) keys.Add($"department:{department:N}");
                foreach (var scope in scopes) keys.Add(scope.ScopeKey.Replace("-", "").ToLowerInvariant());
                var indexes = new List<object>();
                foreach (var key in keys)
                {
                    var scopedProjections = key == "global" ? projections : (await new AuthorizationProjectionBuilder(db).BuildScopeProjectionsAsync(key, cancellationToken)).ToList();
                    indexes.Add(new { key = $"security:scope-index:{key}", keys = scopedProjections.Select(x => x.Key).ToArray() });
                    var scope = scopes.SingleOrDefault(x => x.ScopeKey.Replace("-", "").ToLowerInvariant() == key);
                    var redisKey = key == "global" ? "dms:scope:global" : $"dms:scope:{key.Split(':')[0]}:{Guid.Parse(key.Split(':')[1]):N}";
                    projections.Add(new(redisKey, JsonSerializer.Serialize(new
                    {
                        version = scope?.Version ?? 0, updating = scope?.IsUpdating ?? false, complete = true,
                        operationId = scope?.ActiveOperationId?.ToString("N")
                    }, JsonOptions)));
                }
                if (await readiness.GetCurrentServerRunIdAsync(cancellationToken) != runId) throw new SecurityUnavailableException();
                var result = await redis.ExecuteAsync("finish-bootstrap", new[] { "security:meta", LockKey },
                    new[] { owner, Guid.NewGuid().ToString("N"), runId, "1", JsonSerializer.Serialize(projections, JsonOptions), JsonSerializer.Serialize(indexes, JsonOptions) }, cancellationToken);
                if (result == "snapshot-changed") return false;
                if (result != "ready") throw new SecurityUnavailableException();
                await transaction.CommitAsync(cancellationToken);
                return true;
            });
            if (!published) return false;
            await readiness.AssertReadyAsync(cancellationToken);
            return true;
        }
        finally
        {
            await redis.ExecuteAsync("abort-bootstrap", new[] { LockKey }, new[] { owner }, CancellationToken.None);
        }
    }
}
