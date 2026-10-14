using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Queries.GetFileReadAccess;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace CleanArchCqrs.Infrastructure.Preview;

public sealed class PreviewProvider(AppDbContext db, IOptions<PreviewOptions> options,
    ISender sender, IObjectReadUrlService signer, ReadAccessOptions readOptions, IFileAccessService access) : IPreviewProvider
{
    public async Task<PreviewResult> GetAsync(FileAccessContext context, FileAccessGrant grant, bool enqueue, CancellationToken ct)
    {
        var source = await PreviewSource.LoadAsync(db, context.TenantId, grant.FileId, grant.DocumentVersion, ct);
        if (source is null) return new("unavailable", grant.DocumentVersion, "SCAN_OR_VERSION_NOT_READABLE");
        var mime = source.ContentType;
        if (mime is "application/pdf" or "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "text/plain")
        {
            var inline = await sender.Send(new GetFileReadAccessQuery(grant.FileId, "inline"), ct);
            if (!inline.IsSuccess || inline.Value is null) return new("unsupported", grant.DocumentVersion, "INLINE_UNAVAILABLE");
            if (!PreviewSource.Same(await PreviewSource.LoadAsync(db, context.TenantId, grant.FileId, grant.DocumentVersion, ct), source))
                throw new AuthorizationChangedException();
            var value = inline.Value;
            return new("ready", grant.DocumentVersion, Url: value.Url, ExpiresAt: value.ExpiresAt,
                ContentType: value.ContentType, SupportsRange: value.SupportsRange);
        }
        if (!PreviewOptions.IsOffice(mime)) return new("unsupported", grant.DocumentVersion, "FORMAT_UNSUPPORTED");
        if (!options.Value.Enabled) return new("unsupported", grant.DocumentVersion, "OFFICE_CONVERTER_DISABLED");
        if (source.SizeBytes > PreviewOptions.MaxInputBytes) return new("unsupported", grant.DocumentVersion, "PREVIEW_INPUT_LIMIT");
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == source.ProviderId && p.IsActive, ct);
        if (provider?.Type != StorageProviderType.S3 || readOptions.PublicApiOrigin is null)
            return new("unsupported", grant.DocumentVersion, "PRIVATE_PREVIEW_STORAGE_UNAVAILABLE");
        var cache = options.Value.CacheVersion(source.Hash);
        var artifact = await FindAsync(context.TenantId, grant, cache, ct);
        if (artifact is null && enqueue)
        {
            // Lock the authoritative row before inserting the durable artifact intent. Purging/overwriting
            // cannot commit between this comparison and insertion; worker takes the shared file lock.
            try
            {
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var locked = await db.FileEntries.IgnoreQueryFilters().Where(f => f.Id == grant.FileId
                        && f.TenantId == context.TenantId && f.DocumentVersion == grant.DocumentVersion
                        && f.Hash == source.Hash && f.StorageKey == source.ObjectKey && f.StorageProviderId == source.ProviderId
                        && f.ScanStatus == "Clean" && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted)
                        .ExecuteUpdateAsync(s => s.SetProperty(f => f.Hash, source.Hash), ct);
                    if (locked != 1) throw new AuthorizationChangedException();
                    if (await FindAsync(context.TenantId, grant, cache, ct) is null)
                    {
                        var item = new FilePreviewArtifact { TenantId = context.TenantId, FileId = grant.FileId,
                            DocumentVersion = grant.DocumentVersion, ConverterVersion = cache, Format = "pdf", ProviderId = source.ProviderId };
                        item.ObjectKey = $"previews/{context.TenantId:N}/{grant.FileId:N}/{grant.DocumentVersion}/{item.Id:N}.pdf";
                        db.FilePreviewArtifacts.Add(item);
                        db.DocumentOutboxMessages.Add(new DocumentOutboxMessage { TenantId = context.TenantId,
                            FileId = grant.FileId, DocumentVersion = grant.DocumentVersion, EventType = "PreviewRequested",
                            IdempotencyKey = $"preview:{item.Id:N}", Payload = item.Id.ToString("N") });
                        await db.SaveChangesAsync(ct);
                    }
                    await tx.CommitAsync(ct);
                });
            }
            finally { db.ChangeTracker.Clear(); }
            artifact = await FindAsync(context.TenantId, grant, cache, ct);
        }
        if (artifact is null) return new("notRequested", grant.DocumentVersion);
        if (artifact.Status != FilePreviewStatus.Ready)
        {
            var failed = await db.DocumentOutboxMessages.IgnoreQueryFilters().AnyAsync(m => m.TenantId == context.TenantId
                && m.EventType == "PreviewRequested" && m.IdempotencyKey == "preview:" + artifact.Id.ToString("N")
                && m.Status == DocumentOutboxStatus.Failed, ct);
            return new(failed ? "failed" : "pending", grant.DocumentVersion, failed ? "PREVIEW_PROCESSING_FAILED" : null);
        }
        if (artifact.ExpiresAt.HasValue && artifact.ExpiresAt <= DateTimeOffset.UtcNow)
            return new("failed", grant.DocumentVersion, "PREVIEW_EXPIRED");
        var read = await signer.CreateAsync(new(artifact.ProviderId, artifact.ObjectKey, "preview.pdf", "application/pdf", "inline", readOptions.Lifetime), ct);
        if (Uri.Compare(read.Url, readOptions.PublicApiOrigin, UriComponents.SchemeAndServer, UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase) == 0 || read.Url.Scheme != "https" && !read.Url.IsLoopback)
            return new("unsupported", grant.DocumentVersion, "INLINE_ORIGIN_UNAVAILABLE");
        db.AuditLogs.Add(new AuditLog { TenantId = context.TenantId, UserId = context.Actor.UserId,
            Action = "FilePreviewAccessIssued", EntityName = "FileEntry", EntityId = grant.FileId,
            Details = $"DocumentVersion={grant.DocumentVersion};ProviderId={artifact.ProviderId}" });
        await db.SaveChangesAsync(ct);
        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        if (!PreviewSource.Same(await PreviewSource.LoadAsync(db, context.TenantId, grant.FileId, grant.DocumentVersion, ct), source)
            || !await db.FilePreviewArtifacts.IgnoreQueryFilters().AnyAsync(a => a.Id == artifact.Id
                && a.TenantId == context.TenantId && a.Status == FilePreviewStatus.Ready && a.ObjectKey == artifact.ObjectKey, ct))
            throw new AuthorizationChangedException();
        return new("ready", grant.DocumentVersion, Url: read.Url, ExpiresAt: read.ExpiresAt,
            ContentType: "application/pdf", SupportsRange: read.SupportsRange);
    }
    private Task<FilePreviewArtifact?> FindAsync(Guid tenant, FileAccessGrant grant, string cache, CancellationToken ct) =>
        db.FilePreviewArtifacts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == tenant
            && a.FileId == grant.FileId && a.DocumentVersion == grant.DocumentVersion && a.ConverterVersion == cache && a.Format == "pdf", ct);
}
