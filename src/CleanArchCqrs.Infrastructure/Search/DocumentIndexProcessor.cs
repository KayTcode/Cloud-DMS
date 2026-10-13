using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Search.Extraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.Infrastructure.Search;

public sealed class DocumentIndexProcessor(AppDbContext db, IStorageServiceFactory storageFactory,
    IVirusScannerService scanner, IFileTextExtractor extractor, ITextChunker chunker,
    IDocumentIndexSink sink, IAuthorizationMutationCoordinator coordinator, UploadQuota quota,
    IOptions<DocumentIndexOptions> options)
{
    public async Task ProcessAsync(DocumentOutboxMessage message, Guid leaseOwner, CancellationToken ct)
    {
        var file = await db.FileEntries.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == message.FileId && f.TenantId == message.TenantId, ct);
        if (file is null) return;
        await AssertLeaseAsync(message.Id, leaseOwner, ct);
        // FileEntry is authoritative; event order and event payload never select lifecycle or object.
        if (file.Lifecycle is FileLifecycle.Purging or FileLifecycle.Purged)
        {
            await PurgeAsync(file, message, leaseOwner, ct);
            return;
        }
        if (file.IsDeleted || file.Lifecycle == FileLifecycle.Binned)
        {
            await sink.DeleteAsync(file.TenantId, file.Id, ct);
            await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == file.Id && f.TenantId == file.TenantId
                && (f.IsDeleted || f.Lifecycle == FileLifecycle.Binned))
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ActiveSearchGenerationId, (Guid?)null), ct);
            await CleanupRetiredAsync(file, message, leaseOwner, ct);
            return;
        }
        if (message.DocumentVersion != file.DocumentVersion) return;
        if (message.EventType == "DocumentRetention")
        {
            await CleanupRetiredAsync(file, message, leaseOwner, ct);
            return;
        }
        if (message.EventType == "DocumentPermissionsChanged" && file.ActiveSearchGenerationId.HasValue) return;
        var version = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(v => v.TenantId == file.TenantId && v.FileId == file.Id
                && v.DocumentVersion == file.DocumentVersion, ct);
        if (version is null) throw new InvalidOperationException("CONTENT_VERSION_MISSING");
        if (version.PhysicalStorageDeletedAt.HasValue || version.ObjectKey != file.StorageKey
            || version.ProviderId != file.StorageProviderId) throw new InvalidOperationException("CONTENT_IDENTITY_MISMATCH");
        if (!options.Value.ClamAvCoverageConfirmed || version.SizeBytes > options.Value.MaxScanBytes)
        {
            await PublishScanAsync(file, version, message, leaseOwner, FileScanStatus.Pending,
                version.Hash, "application/octet-stream", "coverage", ct);
            throw new InvalidOperationException("SCAN_AWAITING_VERIFICATION");
        }
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == version.ProviderId, ct);
        var storage = storageFactory.CreateService(provider);
        var path = Path.Combine(Path.GetTempPath(), $"dms-scan-{Guid.NewGuid():N}");
        TextExtractionResult extraction;
        var analysisStarted = DateTimeOffset.UtcNow;
        try
        {
            await using var captured = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                System.IO.FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            using var scanDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            scanDeadline.CancelAfter(TimeSpan.FromSeconds(options.Value.ScanTimeoutSeconds));
            await using var source = await storage.DownloadFileAsync(version.ObjectKey, scanDeadline.Token);
            using var scanning = new ScanCaptureStream(source, captured, options.Value.MaxScanBytes);
            var scan = await scanner.ScanStreamAsync(scanning, scanDeadline.Token);
            if (scanning.BytesRead > options.Value.MaxScanBytes)
            {
                await PublishScanAsync(file, version, message, leaseOwner, FileScanStatus.Pending,
                    version.Hash, "application/octet-stream", "coverage", ct);
                throw new InvalidOperationException("SCAN_AWAITING_VERIFICATION");
            }
            var state = !scan.ScanSucceeded ? FileScanStatus.Failed : scan.IsInfected ? FileScanStatus.Infected : FileScanStatus.Clean;
            if (state == FileScanStatus.Clean && (!scanning.ReachedEnd || scanning.BytesRead != version.SizeBytes))
                state = FileScanStatus.Failed;
            var hash = state == FileScanStatus.Clean ? scanning.FinishHash() : "";
            if (state == FileScanStatus.Clean && version.Hash.Length > 0 && version.Hash != hash)
                state = FileScanStatus.Failed;
            if (state != FileScanStatus.Clean) hash = version.Hash;
            if (state != FileScanStatus.Clean)
            {
                await PublishScanAsync(file, version, message, leaseOwner, state, hash, "application/octet-stream", "scan", ct);
                if (state == FileScanStatus.Infected) return;
                throw new InvalidOperationException("SCAN_FAILED_OR_CONTENT_CHANGED");
            }
            captured.Position = 0;
            analysisStarted = DateTimeOffset.UtcNow;
            try
            {
                extraction = scanning.BytesRead > DocumentIndexOptions.AnalysisBytes
                    ? new("application/octet-stream", [], true, "ANALYSIS_BYTE_LIMIT")
                    : await extractor.ExtractAsync(captured, file.Name, ct);
            }
            catch
            {
                await PublishScanAsync(file, version, message, leaseOwner, FileScanStatus.Clean,
                    hash, "application/octet-stream", "scan", ct);
                throw;
            }
            await PublishScanAsync(file, version, message, leaseOwner, FileScanStatus.Clean,
                hash, extraction.ContentType, "type", ct);
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
        if (!await IsCurrentAsync(file, ct)) return;
        var generation = new FileSearchGeneration
        {
            TenantId = file.TenantId, FileId = file.Id, DocumentVersion = file.DocumentVersion,
            ModelVersion = options.Value.ModelVersion, ExtractorVersion = "bounded-extractor-v1",
            ChunkerVersion = "none", Status = FileSearchStatus.Processing,
            IsPartial = extraction.IsPartial, FailureCode = extraction.Reason
        };
        if (extraction.Sections.Any(s => !string.IsNullOrWhiteSpace(s.Text)))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = TimeSpan.FromSeconds(120) - (DateTimeOffset.UtcNow - analysisStarted);
            if (remaining <= TimeSpan.Zero) throw new OperationCanceledException("ANALYSIS_TIMEOUT");
            deadline.CancelAfter(remaining);
            var result = await chunker.ChunkAsync(extraction.Sections, deadline.Token);
            generation.ModelVersion = result.ModelVersion;
            generation.ChunkerVersion = result.ChunkerVersion;
            generation.IsPartial |= result.IsPartial;
            generation.FailureCode = result.IsPartial ? "CHUNK_LIMIT" : generation.FailureCode;
            db.FileSearchGenerations.Add(generation);
            db.FileTextChunks.AddRange(result.Chunks.Select(c => new FileTextChunk
            {
                TenantId = file.TenantId, GenerationId = generation.Id, Ordinal = c.Ordinal,
                Text = c.Text, TokenCount = c.TokenCount, Location = c.Location
            }));
            await db.SaveChangesAsync(ct);
            await AssertLeaseAsync(message.Id, leaseOwner, ct);
            await sink.StageAsync(generation.Id, ct);
        }
        else
        {
            // Metadata only has an explicit reason and zero chunks. Never mark it semantic-ready.
            generation.IsPartial = true;
            generation.FailureCode ??= "NO_EXTRACTABLE_TEXT";
            db.FileSearchGenerations.Add(generation);
            await db.SaveChangesAsync(ct);
        }
        db.ChangeTracker.Clear();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AssertLeaseAsync(message.Id, leaseOwner, ct);
            var changed = await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == file.Id && f.TenantId == file.TenantId
                && f.DocumentVersion == file.DocumentVersion && f.StorageKey == version.ObjectKey
                && f.StorageProviderId == version.ProviderId && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted
                && f.ScanStatus == "Clean")
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ActiveSearchGenerationId, generation.Id), ct);
            await db.FileSearchGenerations.IgnoreQueryFilters().Where(g => g.Id == generation.Id && g.TenantId == file.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Status, changed == 1
                    ? generation.IsPartial ? FileSearchStatus.Partial : FileSearchStatus.Ready : FileSearchStatus.Superseded)
                    .SetProperty(g => g.PublishedAt, changed == 1 ? DateTimeOffset.UtcNow : (DateTimeOffset?)null), ct);
            await tx.CommitAsync(ct);
        });
        await CleanupRetiredAsync(file, message, leaseOwner, ct);
    }

    private async Task PublishScanAsync(FileEntry file, FileContentVersion version, DocumentOutboxMessage message,
        Guid owner, FileScanStatus state, string hash, string mime, string phase, CancellationToken ct)
    {
        var key = $"index-scan:{message.Id:N}:{message.AttemptCount}:{phase}";
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{version.Id}:{state}:{hash}:{mime}"))).ToLowerInvariant();
        await coordinator.ExecuteAsync(new MutationRequest("global", key, payloadHash), file.OwnerId, async token =>
        {
            await AssertLeaseAsync(message.Id, owner, token);
            var changed = await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == file.Id && f.TenantId == file.TenantId
                && f.DocumentVersion == file.DocumentVersion && f.StorageKey == version.ObjectKey
                && f.StorageProviderId == version.ProviderId && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ScanStatus, state.ToString())
                    .SetProperty(f => f.Hash, hash).SetProperty(f => f.ContentType, mime)
                    .SetProperty(f => f.ActiveSearchGenerationId, (Guid?)null)
                    .SetProperty(f => f.AuthorizationVersion, f => f.AuthorizationVersion + 1), token);
            if (changed != 1) throw new InvalidOperationException("DOCUMENT_SUPERSEDED");
            await db.FileContentVersions.IgnoreQueryFilters().Where(v => v.Id == version.Id && v.TenantId == file.TenantId
                && v.ObjectKey == version.ObjectKey && v.ProviderId == version.ProviderId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ScanStatus, state)
                    .SetProperty(v => v.Hash, hash).SetProperty(v => v.ContentType, mime), token);
        }, ct);
        db.ChangeTracker.Clear();
    }

    internal Task<bool> IsCurrentAsync(FileEntry file, CancellationToken ct) => db.FileEntries.IgnoreQueryFilters()
        .AnyAsync(f => f.Id == file.Id && f.TenantId == file.TenantId && f.DocumentVersion == file.DocumentVersion
            && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted, ct);

    internal async Task AssertLeaseAsync(Guid id, Guid owner, CancellationToken ct)
    {
        var lease = db.DocumentOutboxMessages.IgnoreQueryFilters().Where(m => m.Id == id
            && m.Status == DocumentOutboxStatus.Processing && m.LeaseOwnerId == owner
            && m.LeaseExpiresAt > DateTimeOffset.UtcNow);
        // Inside publication transactions, hold the lease row until commit so a reclaim cannot interleave.
        var owned = db.Database.CurrentTransaction is null ? await lease.AnyAsync(ct)
            : await lease.ExecuteUpdateAsync(s => s.SetProperty(m => m.LeaseOwnerId, owner), ct) == 1;
        if (!owned) throw new InvalidOperationException("LEASE_LOST");
    }

    private async Task PurgeAsync(FileEntry file, DocumentOutboxMessage message, Guid owner, CancellationToken ct)
    {
        await sink.DeleteAsync(file.TenantId, file.Id, ct);
        // URLs already issued can survive until their expiry; allow maximum read TTL before physical deletion.
        if (file.DeletedAt is null || file.DeletedAt > DateTime.UtcNow.AddMinutes(-10))
            throw new InvalidOperationException("READ_URL_RETENTION");
        var versions = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == file.TenantId && v.FileId == file.Id && v.PhysicalStorageDeletedAt == null).ToArrayAsync(ct);
        if (!await db.FileContentVersions.IgnoreQueryFilters().AnyAsync(v => v.TenantId == file.TenantId
            && v.FileId == file.Id && v.DocumentVersion == file.DocumentVersion && v.ObjectKey == file.StorageKey
            && v.ProviderId == file.StorageProviderId, ct)) throw new InvalidOperationException("CONTENT_VERSION_MISSING");
        foreach (var version in versions)
        {
            await AssertLeaseAsync(message.Id, owner, ct);
            if (await db.FileEntries.IgnoreQueryFilters().AnyAsync(f => f.Id != file.Id && f.StorageProviderId == version.ProviderId
                && f.StorageKey == version.ObjectKey && f.Lifecycle != FileLifecycle.Purged, ct))
                throw new InvalidOperationException("CONTENT_IDENTITY_MISMATCH");
            await DeleteObjectAsync(version.ProviderId, version.ObjectKey, ct);
            await RecordPhysicalDeletionAsync(version, ct);
        }
        var artifacts = await db.FilePreviewArtifacts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == file.TenantId && a.FileId == file.Id).ToArrayAsync(ct);
        foreach (var artifact in artifacts)
        {
            await AssertLeaseAsync(message.Id, owner, ct);
            await DeleteObjectAsync(artifact.ProviderId, artifact.ObjectKey, ct);
            await db.FilePreviewArtifacts.IgnoreQueryFilters().Where(a => a.Id == artifact.Id && a.TenantId == file.TenantId).ExecuteDeleteAsync(ct);
        }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AssertLeaseAsync(message.Id, owner, ct);
            await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == file.Id && f.TenantId == file.TenantId
                && (f.Lifecycle == FileLifecycle.Purging || f.Lifecycle == FileLifecycle.Purged))
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ActiveSearchGenerationId, (Guid?)null)
                    .SetProperty(f => f.Lifecycle, FileLifecycle.Purged), ct);
            var ids = db.FileSearchGenerations.IgnoreQueryFilters().Where(g => g.TenantId == file.TenantId && g.FileId == file.Id).Select(g => g.Id);
            await db.FileTextChunks.IgnoreQueryFilters().Where(c => c.TenantId == file.TenantId && ids.Contains(c.GenerationId)).ExecuteDeleteAsync(ct);
            await db.FileSearchGenerations.IgnoreQueryFilters().Where(g => g.TenantId == file.TenantId && g.FileId == file.Id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    internal async Task CleanupRetiredAsync(FileEntry file, DocumentOutboxMessage message, Guid owner, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-options.Value.RetentionMinutes);
        // The replacement time, not the original upload time, starts read URL retention.
        if (await db.FileContentVersions.IgnoreQueryFilters().AnyAsync(v => v.TenantId == file.TenantId
            && v.FileId == file.Id && v.DocumentVersion == file.DocumentVersion && v.CreatedAt >= cutoff, ct)) return;
        var versions = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == file.TenantId && v.FileId == file.Id && v.DocumentVersion < file.DocumentVersion
                && v.CreatedAt < cutoff && v.PhysicalStorageDeletedAt == null).ToArrayAsync(ct);
        foreach (var version in versions)
        {
            await AssertLeaseAsync(message.Id, owner, ct);
            // Also exclude aliases of the current object, including historical legacy keys.
            if (await db.FileEntries.IgnoreQueryFilters().AnyAsync(f => f.StorageProviderId == version.ProviderId
                && f.StorageKey == version.ObjectKey && f.Lifecycle != FileLifecycle.Purged, ct)) continue;
            await DeleteObjectAsync(version.ProviderId, version.ObjectKey, ct);
            await RecordPhysicalDeletionAsync(version, ct);
        }
        var generations = await db.FileSearchGenerations.IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.TenantId == file.TenantId && g.FileId == file.Id && g.CreatedAt < cutoff
                && !db.FileEntries.IgnoreQueryFilters().Any(f => f.ActiveSearchGenerationId == g.Id)).ToArrayAsync(ct);
        foreach (var generation in generations)
        {
            await AssertLeaseAsync(message.Id, owner, ct);
            await sink.DeleteGenerationAsync(file.TenantId, file.Id, generation.Id, ct);
            await db.FileTextChunks.IgnoreQueryFilters().Where(c => c.TenantId == file.TenantId && c.GenerationId == generation.Id).ExecuteDeleteAsync(ct);
            await db.FileSearchGenerations.IgnoreQueryFilters().Where(g => g.Id == generation.Id && g.TenantId == file.TenantId).ExecuteDeleteAsync(ct);
        }
        var artifacts = await db.FilePreviewArtifacts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == file.TenantId && a.FileId == file.Id && a.CreatedAt < cutoff
                && a.DocumentVersion < file.DocumentVersion).ToArrayAsync(ct);
        foreach (var artifact in artifacts)
        {
            await AssertLeaseAsync(message.Id, owner, ct);
            await DeleteObjectAsync(artifact.ProviderId, artifact.ObjectKey, ct);
            await db.FilePreviewArtifacts.IgnoreQueryFilters().Where(a => a.Id == artifact.Id && a.TenantId == file.TenantId).ExecuteDeleteAsync(ct);
        }
    }

    private async Task RecordPhysicalDeletionAsync(FileContentVersion version, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await quota.RecordPhysicalCleanupAsync(version.Id, ct);
            await db.FileContentVersions.IgnoreQueryFilters().Where(v => v.Id == version.Id && v.TenantId == version.TenantId
                && v.PhysicalStorageDeletedAt == null).ExecuteUpdateAsync(s => s.SetProperty(v => v.PhysicalStorageDeletedAt, DateTimeOffset.UtcNow), ct);
            await tx.CommitAsync(ct);
        });
    }

    private async Task DeleteObjectAsync(Guid providerId, string key, CancellationToken ct)
    {
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == providerId, ct);
        var storage = storageFactory.CreateService(provider);
        try
        {
            if (!await storage.DeleteFileAsync(key, ct))
            {
                // Some legacy adapters return false both for absence and errors. Confirm absence before refunding.
                try { await using var existing = await storage.DownloadFileAsync(key, ct); }
                catch (FileNotFoundException) { return; }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound) { return; }
                catch (Microsoft.Kiota.Abstractions.ApiException ex) when (ex.ResponseStatusCode == 404) { return; }
                throw new IOException("PHYSICAL_DELETE_UNCONFIRMED");
            }
        }
        finally { (storage as IDisposable)?.Dispose(); }
    }
}
