using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CleanArchCqrs.Application.Features.Files.Commands.BeginMultipartUpload;

public sealed record BeginMultipartUploadCommand(string FileName, long SizeBytes, Guid? FolderId,
    Guid? ReplaceFileId, string IdempotencyKey) : MediatR.IRequest<MultipartUploadSessionDto>;
public sealed record MultipartUploadSessionDto(Guid SessionId, Guid FileId, long DocumentVersion,
    DateTimeOffset ExpiresAt, long ReservedBytes);
public sealed record PresignMultipartPartCommand(Guid SessionId, int PartNumber)
    : MediatR.IRequest<MultipartPartUrlDto>;
public sealed record MultipartPartUrlDto(Uri Url, DateTimeOffset ExpiresAt);
public sealed record CompleteMultipartUploadCommand(Guid SessionId, IReadOnlyList<MultipartPart> Parts)
    : MediatR.IRequest<MultipartCompletionDto>;
public sealed record MultipartCompletionDto(Guid FileId, long DocumentVersion, MutationReceipt Receipt);
public sealed record AbortMultipartUploadCommand(Guid SessionId) : MediatR.IRequest<bool>;

public sealed class BeginMultipartUploadHandler(MultipartUploadService service)
    : MediatR.IRequestHandler<BeginMultipartUploadCommand, MultipartUploadSessionDto>
{
    public Task<MultipartUploadSessionDto> Handle(BeginMultipartUploadCommand request, CancellationToken ct) =>
        service.BeginAsync(request, ct);
}
public sealed class PresignMultipartPartHandler(MultipartUploadService service)
    : MediatR.IRequestHandler<PresignMultipartPartCommand, MultipartPartUrlDto>
{
    public Task<MultipartPartUrlDto> Handle(PresignMultipartPartCommand request, CancellationToken ct) =>
        service.PresignAsync(request, ct);
}
public sealed class CompleteMultipartUploadHandler(MultipartUploadService service)
    : MediatR.IRequestHandler<CompleteMultipartUploadCommand, MultipartCompletionDto>
{
    public Task<MultipartCompletionDto> Handle(CompleteMultipartUploadCommand request, CancellationToken ct) =>
        service.CompleteAsync(request, ct);
}
public sealed class AbortMultipartUploadHandler(MultipartUploadService service)
    : MediatR.IRequestHandler<AbortMultipartUploadCommand, bool>
{
    public Task<bool> Handle(AbortMultipartUploadCommand request, CancellationToken ct) =>
        service.AbortAsync(request, ct);
}

public sealed class MultipartUploadService(
    IApplicationDbContext db, UploadAuthorization authorization,
    UploadQuota quota, IStorageServiceFactory storageFactory,
    IAuthorizationMutationCoordinator coordinator, IConfiguration configuration)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan PartUrlLifetime = TimeSpan.FromMinutes(10);

    public async Task<MultipartUploadSessionDto> BeginAsync(BeginMultipartUploadCommand command, CancellationToken ct)
    {
        ValidateKey(command.IdempotencyKey);
        var maxBytes = long.TryParse(configuration["Upload:MaxStorageBytes"], out var configuredMaximum)
            ? configuredMaximum : 5L * 1024 * 1024 * 1024;
        if (maxBytes <= 0 || command.SizeBytes <= 0 || command.SizeBytes > maxBytes
            || string.IsNullOrWhiteSpace(command.FileName) || command.FileName.Length > 255)
            throw new ArgumentException("Invalid upload size or name.");
        var requestHash = Hash(JsonSerializer.Serialize(new
        {
            command.FileName, command.SizeBytes, command.FolderId, command.ReplaceFileId
        }));
        var (context, user, provider, _) = await authorization.RequireAsync(command.FolderId, command.ReplaceFileId, ct);
        var prior = await db.FileUploadSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s =>
            s.TenantId == context.TenantId && s.UserId == user.Id && s.IdempotencyKey == command.IdempotencyKey, ct);
        if (prior is not null)
        {
            if (prior.RequestHash != requestHash
                || prior.Status is FileUploadStatus.Failed or FileUploadStatus.Expired)
                throw new InvalidOperationException("Idempotency key conflicts with an existing upload.");
            return ToDto(prior);
        }
        if (provider.Type != StorageProviderType.S3)
            throw new NotSupportedException("Direct multipart upload requires an S3-compatible provider.");
        var fileId = command.ReplaceFileId ?? Guid.NewGuid();
        var version = 1L;
        var quotaUserId = user.Id;
        var quotaDepartmentId = user.DepartmentId;
        if (command.ReplaceFileId.HasValue)
        {
            var old = await db.FileEntries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(f =>
                f.Id == fileId && f.TenantId == context.TenantId && !f.IsDeleted
                && f.Lifecycle == FileLifecycle.Active, ct) ?? throw new AuthorizationChangedException();
            version = checked(old.DocumentVersion + 1);
            quotaUserId = old.OwnerId;
            quotaDepartmentId = old.OwningDepartmentId ?? old.DepartmentId;
        }
        var key = $"objects/{context.TenantId:N}/{fileId:N}/{version}/{Guid.NewGuid():N}";
        using var storage = storageFactory.CreateService(provider) as IMultipartObjectStorage
            ?? throw new NotSupportedException("Provider does not support multipart upload.");
        var uploadId = await storage.BeginAsync(key, "application/octet-stream", ct);
        var session = new FileUploadSession
        {
            TenantId = context.TenantId, UserId = user.Id, QuotaUserId = quotaUserId,
            DepartmentId = quotaDepartmentId,
            FileId = fileId, DocumentVersion = version, PreviousDocumentVersion = command.ReplaceFileId.HasValue ? version - 1 : null,
            ProviderId = provider.Id, ObjectKey = key, MultipartUploadId = uploadId,
            FileName = command.FileName, FolderId = command.FolderId,
            ReservedBytes = command.SizeBytes, IdempotencyKey = command.IdempotencyKey,
            RequestHash = requestHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime)
        };
        var commitAttempted = false;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await quota.ReserveAsync(context.TenantId, quotaDepartmentId, quotaUserId,
                provider.Id, command.SizeBytes, ct);
            db.FileUploadSessions.Add(session);
            await db.SaveChangesAsync(ct);
            commitAttempted = true;
            await tx.CommitAsync(ct);
        }
        catch
        {
            // An uncertain commit needs reconciliation; aborting could destroy a committed session.
            if (!commitAttempted)
            {
                try { await storage.AbortAsync(key, uploadId, CancellationToken.None); }
                catch { /* An expired orphan is cleaned by the storage lifecycle policy. */ }
            }
            throw;
        }
        return ToDto(session);
    }

    public async Task<MultipartPartUrlDto> PresignAsync(PresignMultipartPartCommand command, CancellationToken ct)
    {
        if (command.PartNumber is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(command.PartNumber));
        var session = await RequireSessionAsync(command.SessionId, ct);
        if (session.Status != FileUploadStatus.Pending || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Upload session is not open.");
        using var storage = await StorageAsync(session, ct);
        var expiresAt = DateTimeOffset.UtcNow.Add(PartUrlLifetime);
        if (expiresAt > session.ExpiresAt) expiresAt = session.ExpiresAt;
        return new(await storage.PresignPartAsync(session.ObjectKey, session.MultipartUploadId,
            command.PartNumber, expiresAt - DateTimeOffset.UtcNow, ct), expiresAt);
    }

    public async Task<MultipartCompletionDto> CompleteAsync(CompleteMultipartUploadCommand command, CancellationToken ct)
    {
        var session = await RequireSessionAsync(command.SessionId, ct);
        if (command.Parts.Count is < 1 or > 10000
            || command.Parts.Select(p => p.Number).Distinct().Count() != command.Parts.Count
            || command.Parts.OrderBy(p => p.Number).Select((p, i) => p.Number == i + 1).Any(ok => !ok)
            || command.Parts.Any(p => string.IsNullOrWhiteSpace(p.ETag) || p.ETag.Length > 128))
            throw new ArgumentException("Invalid multipart part list.");
        var orderedParts = command.Parts.OrderBy(p => p.Number).ToArray();
        var manifestHash = Hash(JsonSerializer.Serialize(orderedParts));
        if (session.Status == FileUploadStatus.Completed)
        {
            if (session.CompletionManifestHash != manifestHash)
                throw new InvalidOperationException("Completion manifest conflicts with the committed upload.");
            var old = await db.AuthorizationOperations.AsNoTracking().SingleAsync(o =>
                o.ScopeKey == "global" && o.IdempotencyKey == OperationKey(session), ct);
            return new(session.FileId, session.DocumentVersion,
                new MutationReceipt(old.Id, old.Version, old.Status));
        }
        if (session.Status is FileUploadStatus.Failed or FileUploadStatus.Expired
            || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Upload session has expired or was aborted.");
        var (context, user, _, stamps) = await authorization.RequireAsync(session.FolderId,
            session.PreviousDocumentVersion.HasValue ? session.FileId : null, ct);
        var bound = await db.FileUploadSessions.IgnoreQueryFilters()
            .Where(s => s.Id == session.Id && s.Status == FileUploadStatus.Pending
                && s.ExpiresAt > DateTimeOffset.UtcNow
                && (s.CompletionManifestHash == null || s.CompletionManifestHash == manifestHash))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletionManifestHash, manifestHash), ct);
        if (bound != 1)
            throw new InvalidOperationException("Completion manifest conflicts with this upload session.");
        using var storage = await StorageAsync(session, ct);
        var head = await storage.InspectAsync(session.ObjectKey, ct);
        if (head is null)
        {
            try
            {
                await storage.CompleteAsync(session.ObjectKey, session.MultipartUploadId, orderedParts, ct);
            }
            catch
            {
                // S3 may have committed while the response was lost. HEAD decides recovery.
                if (await storage.InspectAsync(session.ObjectKey, ct) is null) throw;
            }
            head = await storage.InspectAsync(session.ObjectKey, ct);
        }
        if (head is null || head.SizeBytes <= 0 || head.SizeBytes > session.ReservedBytes
            || !string.Equals(head.ContentType, session.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Completed object does not match the reserved upload.");

        var request = new MutationRequest("global", OperationKey(session),
            Hash($"multipart:{session.Id:N}:{session.FileId:N}:{session.DocumentVersion}:{head.SizeBytes}:{manifestHash}"));
        var receipt = await coordinator.ExecuteAsync(request, session.UserId, async token =>
        {
            await authorization.AssertCurrentAsync(context, user, stamps, token);
            var claimed = await db.FileUploadSessions.IgnoreQueryFilters()
                .Where(s => s.Id == session.Id && s.TenantId == session.TenantId
                    && s.Status == FileUploadStatus.Pending && s.CompletionManifestHash == manifestHash
                    && s.ExpiresAt > DateTimeOffset.UtcNow
                    && s.ReservedBytes >= head.SizeBytes)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FileUploadStatus.Completed), token);
            if (claimed != 1)
                throw new InvalidOperationException("Upload session is no longer open.");
            var current = await db.FileUploadSessions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(s => s.Id == session.Id, token);
            FileEntry file;
            if (current.PreviousDocumentVersion.HasValue)
            {
                file = await db.FileEntries.IgnoreQueryFilters().SingleAsync(f =>
                    f.TenantId == current.TenantId && f.Id == current.FileId, token);
                if (file.DocumentVersion != current.PreviousDocumentVersion || file.IsDeleted
                    || file.Lifecycle != FileLifecycle.Active || file.OwnerId != current.QuotaUserId)
                    throw new AuthorizationChangedException();
                await quota.ReleaseAsync(current.TenantId,
                    file.OwningDepartmentId ?? file.DepartmentId, file.OwnerId,
                    file.StorageProviderId ?? current.ProviderId, file.SizeBytes, token);
                file.DocumentVersion = current.DocumentVersion;
                file.AuthorizationVersion = checked(file.AuthorizationVersion + 1);
                file.ActiveSearchGenerationId = null;
                file.StorageProviderId = current.ProviderId;
            }
            else
            {
                file = new FileEntry
                {
                    Id = current.FileId, TenantId = current.TenantId, OwnerId = current.QuotaUserId,
                    DepartmentId = current.DepartmentId, OwningDepartmentId = current.DepartmentId,
                    FolderId = current.FolderId, Name = current.FileName,
                    StorageProviderId = current.ProviderId
                };
                db.FileEntries.Add(file);
            }
            file.StorageKey = current.ObjectKey;
            file.SizeBytes = head.SizeBytes;
            file.ContentType = head.ContentType;
            file.Hash = null;
            file.ScanStatus = "Pending";
            db.FileContentVersions.Add(new FileContentVersion
            {
                TenantId = current.TenantId, FileId = current.FileId,
                DocumentVersion = current.DocumentVersion, ObjectKey = current.ObjectKey,
                ProviderId = current.ProviderId, ContentType = head.ContentType,
                SizeBytes = head.SizeBytes, Hash = string.Empty, ScanStatus = FileScanStatus.Pending
            });
            if (head.SizeBytes < current.ReservedBytes)
                await quota.ReleaseAsync(current.TenantId, current.DepartmentId, current.QuotaUserId,
                    current.ProviderId, current.ReservedBytes - head.SizeBytes, token);
            db.DocumentOutboxMessages.Add(new DocumentOutboxMessage
            {
                TenantId = current.TenantId, FileId = current.FileId,
                DocumentVersion = current.DocumentVersion,
                EventType = current.PreviousDocumentVersion.HasValue ? "DocumentContentChanged" : "DocumentCreated",
                IdempotencyKey = $"multipart:{current.Id:N}", Payload = "{}"
            });
        }, ct);
        return new(session.FileId, session.DocumentVersion, receipt);
    }

    public async Task<bool> AbortAsync(AbortMultipartUploadCommand command, CancellationToken ct)
    {
        var session = await RequireSessionAsync(command.SessionId, ct);
        if (session.Status == FileUploadStatus.Completed) return false;
        if (session.Status is FileUploadStatus.Failed or FileUploadStatus.Expired) return true;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            var updated = await db.FileUploadSessions.IgnoreQueryFilters()
                .Where(s => s.Id == session.Id && s.Status == FileUploadStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, FileUploadStatus.Failed), ct);
            if (updated == 1)
                await quota.ReleaseAsync(session.TenantId, session.DepartmentId, session.QuotaUserId,
                    session.ProviderId, session.ReservedBytes, ct);
            await tx.CommitAsync(ct);
            if (updated != 1) return false;
        }
        using var storage = await StorageAsync(session, ct);
        if (await storage.InspectAsync(session.ObjectKey, ct) is not null)
            await ((IStorageService)storage).DeleteFileAsync(session.ObjectKey, ct);
        else
            await storage.AbortAsync(session.ObjectKey, session.MultipartUploadId, ct);
        await db.FileUploadSessions.IgnoreQueryFilters().Where(s => s.Id == session.Id
                && s.Status == FileUploadStatus.Failed && s.StorageCleanedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StorageCleanedAt, DateTimeOffset.UtcNow), ct);
        return true;
    }

    private async Task<FileUploadSession> RequireSessionAsync(Guid id, CancellationToken ct)
    {
        var context = await authorization.RequireAsync(null, null, ct);
        return await db.FileUploadSessions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s =>
            s.Id == id && s.TenantId == context.Context.TenantId && s.UserId == context.User.Id, ct)
            ?? throw new UnauthorizedAccessException("Upload session is unavailable.");
    }

    private async Task<IMultipartObjectStorage> StorageAsync(FileUploadSession session, CancellationToken ct)
    {
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == session.ProviderId && p.IsActive, ct);
        return storageFactory.CreateService(provider) as IMultipartObjectStorage
            ?? throw new NotSupportedException("Provider does not support multipart upload.");
    }

    private static MultipartUploadSessionDto ToDto(FileUploadSession s) =>
        new(s.Id, s.FileId, s.DocumentVersion, s.ExpiresAt, s.ReservedBytes);
    private static string OperationKey(FileUploadSession s) => Hash($"multipart-complete:{s.Id:N}");
    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new ArgumentException("A valid Idempotency-Key is required.");
    }
}
