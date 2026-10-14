using System.Security.Cryptography;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace CleanArchCqrs.Infrastructure.Preview;

// Invoked only by DocumentIndexWorker while holding dms-index:{fileId:N} and a renewable outbox lease.
public sealed class PreviewJobProcessor(AppDbContext db, IStorageServiceFactory factory,
    PreviewConverterClient converter, DocumentIndexProcessor leases, IOptions<PreviewOptions> options)
{
    public async Task ProcessAsync(DocumentOutboxMessage message, Guid owner, CancellationToken ct)
    {
        if (!Guid.TryParseExact(message.Payload, "N", out var artifactId)) throw new InvalidOperationException("PREVIEW_INTENT_INVALID");
        var artifact = await db.FilePreviewArtifacts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == artifactId && a.TenantId == message.TenantId && a.FileId == message.FileId
            && a.DocumentVersion == message.DocumentVersion, ct);
        if (artifact is null) return; // Purge already removed the intent. Never recreate it from event payload.
        var source = await PreviewSource.LoadAsync(db, message.TenantId, message.FileId, message.DocumentVersion, ct);
        if (source is null) return;
        if (!options.Value.Enabled || !PreviewOptions.IsOffice(source.ContentType)
            || source.SizeBytes is <= 0 or > PreviewOptions.MaxInputBytes
            || artifact.ConverterVersion != options.Value.CacheVersion(source.Hash)
            || artifact.ProviderId != source.ProviderId || artifact.Format != "pdf"
            || artifact.ObjectKey != $"previews/{message.TenantId:N}/{message.FileId:N}/{message.DocumentVersion}/{artifact.Id:N}.pdf")
            throw new InvalidOperationException("PREVIEW_IDENTITY_MISMATCH");
        if (artifact.Status == FilePreviewStatus.Ready) return;
        await leases.AssertLeaseAsync(message.Id, owner, ct);
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == source.ProviderId && p.IsActive && p.Type == StorageProviderType.S3, ct);
        var storage = factory.CreateService(provider);
        try
        {
            if (storage is not IPrivateArtifactStorage privateStorage) throw new InvalidOperationException("PREVIEW_STORAGE_UNSUPPORTED");
            await db.FilePreviewArtifacts.IgnoreQueryFilters().Where(a => a.Id == artifact.Id && a.TenantId == message.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, FilePreviewStatus.Processing), ct);
            var path = Path.Combine(Path.GetTempPath(), $"dms-preview-{Guid.NewGuid():N}");
            byte[] output;
            try
            {
                await using var captured = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                    System.IO.FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                using var sourceDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                sourceDeadline.CancelAfter(TimeSpan.FromSeconds(120));
                await using (var input = await storage.DownloadFileAsync(source.ObjectKey, sourceDeadline.Token))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    long count = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, sourceDeadline.Token)) != 0)
                    {
                        count += read;
                        if (count > source.SizeBytes || count > PreviewOptions.MaxInputBytes) throw new InvalidDataException("PREVIEW_INPUT_LIMIT");
                        hash.AppendData(buffer, 0, read);
                        await captured.WriteAsync(buffer.AsMemory(0, read), sourceDeadline.Token);
                    }
                    if (count != source.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(source.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PREVIEW_SOURCE_CHANGED");
                }
                if (!PreviewSource.Same(await PreviewSource.LoadAsync(db, message.TenantId, message.FileId, message.DocumentVersion, ct), source))
                    return;
                captured.Position = 0;
                output = await converter.ConvertAsync(captured, source.ContentType, source.Hash, ct);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            await leases.AssertLeaseAsync(message.Id, owner, ct);
            if (!PreviewSource.Same(await PreviewSource.LoadAsync(db, message.TenantId, message.FileId, message.DocumentVersion, ct), source))
                return;
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await leases.AssertLeaseAsync(message.Id, owner, ct);
                // Hold the authoritative row and the durable lease through the storage write and publication.
                // Lifecycle mutations cannot overtake the final current-version/hash comparison.
                var current = await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == message.FileId
                    && f.TenantId == message.TenantId && f.DocumentVersion == message.DocumentVersion
                    && f.Hash == source.Hash && f.StorageKey == source.ObjectKey && f.StorageProviderId == source.ProviderId
                    && f.ScanStatus == "Clean" && !f.IsDeleted && f.Lifecycle == FileLifecycle.Active)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.Hash, source.Hash), ct);
                if (current != 1) { await tx.RollbackAsync(ct); return; }
                if (!PreviewSource.Same(await PreviewSource.LoadAsync(db, message.TenantId, message.FileId, message.DocumentVersion, ct), source))
                    throw new InvalidOperationException("DOCUMENT_SUPERSEDED");
                using var uploadDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                uploadDeadline.CancelAfter(TimeSpan.FromSeconds(60));
                using var pdf = new MemoryStream(output, false);
                await privateStorage.PutPrivateArtifactAsync(artifact.ObjectKey, pdf, uploadDeadline.Token);
                await leases.AssertLeaseAsync(message.Id, owner, ct);
                var changed = await db.FilePreviewArtifacts.IgnoreQueryFilters().Where(a => a.Id == artifact.Id
                    && a.TenantId == message.TenantId && a.ObjectKey == artifact.ObjectKey
                    && a.ConverterVersion == artifact.ConverterVersion)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, FilePreviewStatus.Ready), ct);
                if (changed != 1) throw new InvalidOperationException("PREVIEW_INTENT_LOST");
                await tx.CommitAsync(ct);
            });
        }
        finally { (storage as IDisposable)?.Dispose(); }
    }
}
