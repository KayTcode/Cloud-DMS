using System.Data;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Workers;

public sealed class MultipartUploadRecoveryWorker(IServiceScopeFactory scopes,
    ILogger<MultipartUploadRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await RecoverAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Multipart upload recovery failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quota = scope.ServiceProvider.GetRequiredService<UploadQuota>();
        var factory = scope.ServiceProvider.GetRequiredService<IStorageServiceFactory>();
        var now = DateTimeOffset.UtcNow;
        var sessions = await db.FileUploadSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.MultipartUploadId != ""
                && (s.Status == FileUploadStatus.Pending && s.ExpiresAt <= now
                    || s.Status == FileUploadStatus.Completing && s.CompletionLeaseExpiresAt <= now
                    || (s.Status == FileUploadStatus.Failed || s.Status == FileUploadStatus.Expired)
                    && (s.StorageCleanedAt == null
                        || s.CompletionManifestHash != null && s.ExpiresAt > now.AddDays(-2))))
            .OrderBy(s => s.StorageCleanedAt).Take(50).ToArrayAsync(ct);
        foreach (var session in sessions)
        {
            try
            {
                if (session.Status is FileUploadStatus.Pending or FileUploadStatus.Completing)
                {
                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                    var claimed = await db.FileUploadSessions.IgnoreQueryFilters()
                        .Where(s => s.Id == session.Id
                            && (s.Status == FileUploadStatus.Pending && s.ExpiresAt <= now
                                || s.Status == FileUploadStatus.Completing && s.CompletionLeaseExpiresAt <= now))
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FileUploadStatus.Expired), ct);
                    if (claimed == 1)
                    {
                        await quota.ReleaseAsync(session.TenantId, session.DepartmentId, session.QuotaUserId,
                            session.ProviderId, session.QuotaReservedBytes, ct, session.ProviderReservedBytes);
                        await quota.ReleaseStagingAsync(session.ProviderId, session.ReservedBytes, ct);
                    }
                    await tx.CommitAsync(ct);
                    if (claimed != 1) continue;
                }
                var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(p => p.Id == session.ProviderId, ct);
                using var storage = factory.CreateService(provider) as IMultipartObjectStorage;
                if (storage is null) continue;
                if (await storage.InspectAsync(session.ObjectKey, ct) is not null)
                    await ((IStorageService)storage).DeleteFileAsync(session.ObjectKey, ct);
                else
                    await storage.AbortAsync(session.ObjectKey, session.MultipartUploadId, ct);
                await db.FileUploadSessions.IgnoreQueryFilters().Where(s => s.Id == session.Id
                        && s.Status != FileUploadStatus.Completed)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.StorageCleanedAt, DateTimeOffset.UtcNow), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Multipart upload cleanup will be retried for session {SessionId}.", session.Id);
            }
        }

        // A storage initiation can succeed before its SQL reservation commits.
        // Reconcile aged S3 uploads that have no durable session at all.
        var providers = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Type == CleanArchCqrs.Domain.Entities.StorageProviderType.S3)
            .ToArrayAsync(ct);
        foreach (var provider in providers)
        {
            try
            {
                using var storage = factory.CreateService(provider) as IMultipartObjectStorage;
                if (storage is null) continue;
                var incomplete = await storage.ListIncompleteAsync(now.AddHours(-2), ct);
                foreach (var upload in incomplete)
                {
                    var durable = await db.FileUploadSessions.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(s => s.ProviderId == provider.Id && s.ObjectKey == upload.Key
                            && s.MultipartUploadId == upload.UploadId, ct);
                    if (!durable)
                        await storage.AbortAsync(upload.Key, upload.UploadId, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Aged multipart uploads could not be reconciled for provider {ProviderId}.", provider.Id);
            }
        }
        await RecoverLegacyAsync(db, factory, now, ct);
    }

    private async Task RecoverLegacyAsync(AppDbContext db, IStorageServiceFactory factory,
        DateTimeOffset now, CancellationToken ct)
    {
        var intents = await db.LegacyUploadIntents.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Status == "Pending" && i.ExpiresAt <= now
                || i.Status == "Abandoned" && i.ExpiresAt > now.AddDays(-2))
            .OrderBy(i => i.StorageCleanedAt).Take(50).ToArrayAsync(ct);
        foreach (var intent in intents)
        {
            try
            {
                if (intent.Status == "Pending")
                {
                    var claimed = await db.LegacyUploadIntents.IgnoreQueryFilters()
                        .Where(i => i.Id == intent.Id && i.Status == "Pending" && i.ExpiresAt <= now)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, "Abandoned"), ct);
                    if (claimed != 1) continue;
                }
                // An abandoned intent cannot become committed by the callback; preserve only an object that
                // an authoritative content version actually references.
                var referencedKeys = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
                    .Where(v => v.FileId == intent.FileId && v.TenantId == intent.TenantId
                        && v.ProviderId == intent.ProviderId
                        && db.FileEntries.IgnoreQueryFilters().Any(f => f.Id == v.FileId
                            && f.TenantId == intent.TenantId))
                    .Select(v => v.ObjectKey).ToArrayAsync(ct);
                var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(p => p.Id == intent.ProviderId, ct);
                var storage = factory.CreateService(provider);
                using var storageLease = storage as IDisposable;
                if (storage is not IIntentObjectStorage inventory) continue;
                var keys = await inventory.FindIntentObjectsAsync(intent.Id, intent.ParentFolderId, ct);
                foreach (var key in keys)
                    if (!referencedKeys.Contains(key, StringComparer.Ordinal)
                        && !await storage.DeleteFileAsync(key, ct))
                        throw new InvalidOperationException("Legacy orphan deletion did not succeed.");
                await db.LegacyUploadIntents.IgnoreQueryFilters().Where(i => i.Id == intent.Id
                        && i.Status == "Abandoned")
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.StorageCleanedAt, DateTimeOffset.UtcNow), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Legacy upload cleanup will be retried for intent {IntentId}.", intent.Id);
            }
        }
    }
}
