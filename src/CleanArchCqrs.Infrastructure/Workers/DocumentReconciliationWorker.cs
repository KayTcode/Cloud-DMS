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

public sealed class DocumentReconciliationWorker(IServiceScopeFactory scopes, IOptions<DocumentIndexOptions> options,
    ILogger<DocumentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try { await ReconcileAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Document reconciliation deferred."); }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        for (var offset = 0; ; offset += 100)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var files = await db.FileEntries.IgnoreQueryFilters().AsNoTracking().OrderBy(f => f.Id).Skip(offset).Take(100).ToArrayAsync(ct);
            if (files.Length == 0) break;
            foreach (var file in files)
            {
                try
                {
                    await BackfillAsync(db, file, ct);
                    var pending = await db.DocumentOutboxMessages.IgnoreQueryFilters().AnyAsync(m => m.TenantId == file.TenantId
                        && m.FileId == file.Id && m.DocumentVersion == file.DocumentVersion
                        && (m.Status == DocumentOutboxStatus.Pending || m.Status == DocumentOutboxStatus.Processing), ct);
                    if (pending) continue;
                    if (file.Lifecycle == FileLifecycle.Active && await db.DocumentOutboxMessages.IgnoreQueryFilters()
                        .AnyAsync(m => m.TenantId == file.TenantId && m.FileId == file.Id && m.DocumentVersion == file.DocumentVersion
                            && m.FailureCode == "SCAN_AWAITING_VERIFICATION" && m.Payload == options.Value.CoverageKey, ct)) continue;
                    var incomplete = file.Lifecycle == FileLifecycle.Purging
                        || file.Lifecycle == FileLifecycle.Active && !file.IsDeleted && (file.ActiveSearchGenerationId == null
                            || !await db.FileSearchGenerations.IgnoreQueryFilters().AnyAsync(g => g.Id == file.ActiveSearchGenerationId
                                && g.TenantId == file.TenantId && g.DocumentVersion == file.DocumentVersion
                                && (g.Status == FileSearchStatus.Ready || g.Status == FileSearchStatus.Partial), ct));
                    var cleanup = await db.FileContentVersions.IgnoreQueryFilters().AnyAsync(v => v.TenantId == file.TenantId
                        && v.FileId == file.Id && v.DocumentVersion < file.DocumentVersion && v.PhysicalStorageDeletedAt == null, ct)
                        || await db.FileSearchGenerations.IgnoreQueryFilters().AnyAsync(g => g.TenantId == file.TenantId
                            && g.FileId == file.Id && g.Id != file.ActiveSearchGenerationId, ct)
                        || await db.FilePreviewArtifacts.IgnoreQueryFilters().AnyAsync(a => a.TenantId == file.TenantId
                            && a.FileId == file.Id && a.DocumentVersion < file.DocumentVersion, ct);
                    if (!incomplete && !cleanup) continue;
                    var window = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 1800;
                    var key = $"reconcile:{file.Id:N}:{file.DocumentVersion}:{window}";
                    if (await db.DocumentOutboxMessages.IgnoreQueryFilters().AnyAsync(m => m.TenantId == file.TenantId && m.IdempotencyKey == key, ct)) continue;
                    db.DocumentOutboxMessages.Add(new DocumentOutboxMessage
                    {
                        TenantId = file.TenantId, FileId = file.Id, DocumentVersion = file.DocumentVersion,
                        EventType = incomplete ? "DocumentReconcile" : "DocumentRetention", IdempotencyKey = key, Payload = "{}"
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { logger.LogWarning("Document reconciliation for {FileId} deferred.", file.Id); }
                finally { db.ChangeTracker.Clear(); }
            }
        }
    }

    private static async Task BackfillAsync(AppDbContext db, FileEntry file, CancellationToken ct)
    {
        if (file.Lifecycle == FileLifecycle.Purged || await db.FileContentVersions.IgnoreQueryFilters()
            .AnyAsync(v => v.TenantId == file.TenantId && v.FileId == file.Id && v.DocumentVersion == file.DocumentVersion, ct)) return;
        if (file.StorageProviderId is null || string.IsNullOrWhiteSpace(file.StorageKey)
            || !await db.StorageProviders.IgnoreQueryFilters().AnyAsync(p => p.Id == file.StorageProviderId, ct))
        {
            var key = $"backfill-unavailable:{file.Id:N}:{file.DocumentVersion}";
            if (!await db.DocumentOutboxMessages.IgnoreQueryFilters().AnyAsync(m => m.TenantId == file.TenantId && m.IdempotencyKey == key, ct))
            {
                db.DocumentOutboxMessages.Add(new()
                {
                    TenantId = file.TenantId, FileId = file.Id, DocumentVersion = file.DocumentVersion,
                    EventType = "DocumentReconcile", IdempotencyKey = key, Status = DocumentOutboxStatus.Failed,
                    FailureCode = "AUTHORITATIVE_OBJECT_UNAVAILABLE", Payload = "{}"
                });
                await db.SaveChangesAsync(ct);
            }
            return;
        }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Lock the authoritative file row and compare immutable source before creating a Pending version.
            var held = await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == file.Id && f.TenantId == file.TenantId
                && f.DocumentVersion == file.DocumentVersion && f.StorageKey == file.StorageKey
                && f.StorageProviderId == file.StorageProviderId && f.Lifecycle != FileLifecycle.Purged)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.DocumentVersion, f => f.DocumentVersion), ct);
            if (held == 1 && !await db.FileContentVersions.IgnoreQueryFilters().AnyAsync(v => v.TenantId == file.TenantId
                && v.FileId == file.Id && v.DocumentVersion == file.DocumentVersion, ct))
            {
                db.FileContentVersions.Add(new FileContentVersion
                {
                    TenantId = file.TenantId, FileId = file.Id, DocumentVersion = file.DocumentVersion,
                    ProviderId = file.StorageProviderId!.Value, ObjectKey = file.StorageKey,
                    SizeBytes = file.SizeBytes, ContentType = "application/octet-stream", ScanStatus = FileScanStatus.Pending
                });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        });
    }
}
