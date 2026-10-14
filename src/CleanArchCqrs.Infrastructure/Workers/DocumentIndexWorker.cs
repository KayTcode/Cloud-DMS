using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.Infrastructure.Workers;

public sealed class DocumentIndexWorker(IServiceScopeFactory scopes, IOptions<DocumentIndexOptions> options,
    ILogger<DocumentIndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await RunOneAsync(ct)) await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) // Never log storage/HTTP exceptions containing keys, URLs or document text.
            {
                logger.LogWarning("Document worker iteration failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task<bool> RunOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        await db.DocumentOutboxMessages.IgnoreQueryFilters().Where(m => m.Status == DocumentOutboxStatus.Processing
            && m.LeaseExpiresAt <= now && m.AttemptCount >= 8)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, DocumentOutboxStatus.Failed)
                .SetProperty(m => m.FailureCode, "LEASE_EXHAUSTED"), ct);
        var candidate = await db.DocumentOutboxMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.AttemptCount < 8 && m.NextAttemptAt <= now && (m.Status == DocumentOutboxStatus.Pending
                || m.Status == DocumentOutboxStatus.Processing && m.LeaseExpiresAt <= now))
            .OrderBy(m => m.NextAttemptAt).ThenBy(m => m.CreatedAt).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;
        // A dedicated connection owns the per-file lock through scan/stage/delete. Different events cannot
        // concurrently delete a restored generation. Mutations still use SQL lifecycle/version comparisons.
        using var lockScope = scopes.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await lockDb.Database.OpenConnectionAsync(ct);
        var connection = lockDb.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, 0)";
        var name = command.CreateParameter(); name.ParameterName = "@name"; name.Value = $"dms-index:{candidate.FileId:N}";
        command.Parameters.Add(name);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
        {
            // Move all eligible events for the busy file briefly out of the selection window. Do not
            // consume attempts or touch live leases; immediately let this worker select another file.
            await db.DocumentOutboxMessages.IgnoreQueryFilters().Where(m => m.TenantId == candidate.TenantId
                && m.FileId == candidate.FileId && m.NextAttemptAt <= now && m.AttemptCount < 8
                && (m.Status == DocumentOutboxStatus.Pending
                    || m.Status == DocumentOutboxStatus.Processing && m.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, now.AddSeconds(5)), ct);
            return true;
        }
        try
        {
            var owner = Guid.NewGuid();
            if (await db.DocumentOutboxMessages.IgnoreQueryFilters().Where(m => m.Id == candidate.Id && m.AttemptCount < 8
                && m.NextAttemptAt <= now && (m.Status == DocumentOutboxStatus.Pending
                    || m.Status == DocumentOutboxStatus.Processing && m.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, DocumentOutboxStatus.Processing)
                    .SetProperty(m => m.LeaseOwnerId, owner).SetProperty(m => m.LeaseExpiresAt, now.AddMinutes(3))
                    .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1), ct) != 1) return true;
            candidate.AttemptCount++;
            using var job = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeat = RenewAsync(candidate.Id, owner, command, job, heartbeatStop.Token);
            string? failure = null;
            try
            {
                if (candidate.EventType == "PreviewRequested")
                    await scope.ServiceProvider.GetRequiredService<CleanArchCqrs.Infrastructure.Preview.PreviewJobProcessor>()
                        .ProcessAsync(candidate, owner, job.Token);
                else
                    await scope.ServiceProvider.GetRequiredService<DocumentIndexProcessor>().ProcessAsync(candidate, owner, job.Token);
            }
            catch (Exception ex)
            {
                failure = FailureCode(ex);
                logger.LogWarning("Document job {MessageId} attempt {Attempt} deferred: {Code}.", candidate.Id, candidate.AttemptCount, failure);
            }
            finally
            {
                heartbeatStop.Cancel();
                try { await heartbeat; } catch (OperationCanceledException) { }
            }
            db.ChangeTracker.Clear();
            var finished = DateTimeOffset.UtcNow;
            await db.DocumentOutboxMessages.IgnoreQueryFilters().Where(m => m.Id == candidate.Id
                && m.LeaseOwnerId == owner && m.Status == DocumentOutboxStatus.Processing && m.LeaseExpiresAt > finished)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, failure == null ? DocumentOutboxStatus.Completed
                        : candidate.AttemptCount >= 8 || failure == "SCAN_AWAITING_VERIFICATION" ? DocumentOutboxStatus.Failed : DocumentOutboxStatus.Pending)
                    .SetProperty(m => m.CompletedAt, failure == null ? finished : (DateTimeOffset?)null)
                    .SetProperty(m => m.FailureCode, failure)
                    .SetProperty(m => m.Payload, failure == "SCAN_AWAITING_VERIFICATION" ? options.Value.CoverageKey : candidate.Payload)
                    .SetProperty(m => m.NextAttemptAt, finished.AddSeconds(Math.Min(300, 5 * Math.Pow(2, candidate.AttemptCount - 1))))
                    .SetProperty(m => m.LeaseOwnerId, (Guid?)null).SetProperty(m => m.LeaseExpiresAt, (DateTimeOffset?)null), ct);
            return true;
        }
        finally
        {
            command.CommandText = "SELECT RELEASE_LOCK(@name)";
            try { await command.ExecuteScalarAsync(CancellationToken.None); } catch { }
        }
    }

    private async Task RenewAsync(Guid id, Guid owner, System.Data.Common.DbCommand lockCommand,
        CancellationTokenSource job, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(ct))
            {
                lockCommand.CommandText = "SELECT IS_USED_LOCK(@name) = CONNECTION_ID()";
                if (Convert.ToInt32(await lockCommand.ExecuteScalarAsync(ct)) != 1) { job.Cancel(); return; }
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                if (await db.DocumentOutboxMessages.IgnoreQueryFilters().Where(m => m.Id == id
                    && m.LeaseOwnerId == owner && m.Status == DocumentOutboxStatus.Processing && m.LeaseExpiresAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeaseExpiresAt, now.AddMinutes(3)), ct) != 1)
                { job.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { job.Cancel(); }
    }

    private static string FailureCode(Exception ex) => ex switch
    {
        OperationCanceledException => "CANCELLED_OR_TIMEOUT",
        HttpRequestException => "MODEL_OR_INDEX_UNAVAILABLE",
        System.ComponentModel.Win32Exception => "PDF_EXTRACTOR_UNAVAILABLE",
        InvalidOperationException when ex.Message is "SCAN_FAILED_OR_CONTENT_CHANGED" or "CONTENT_VERSION_MISSING"
            or "CONTENT_IDENTITY_MISMATCH" or "DOCUMENT_SUPERSEDED" or "LEASE_LOST" or "READ_URL_RETENTION"
            or "INDEX_SINK_UNAVAILABLE" or "SCAN_AWAITING_VERIFICATION" => ex.Message,
        _ => "INDEX_DEPENDENCY_OR_PROCESSING_FAILURE"
    };
}
