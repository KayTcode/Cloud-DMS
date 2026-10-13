using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetFileReadAccess;

public sealed record ReadAccessOptions(TimeSpan Lifetime);
public sealed record GetFileReadAccessQuery(Guid FileId, string Purpose, Uri? ApiOrigin = null) : IRequest<Result<FileReadAccessResult>>;
public sealed record FileReadAccessResult(Uri Url, DateTimeOffset ExpiresAt, long DocumentVersion,
    string ContentType, long SizeBytes, bool SupportsRange);

public sealed class GetFileReadAccessQueryHandler(
    IFileAccessContextResolver contextResolver, IFileAccessService access,
    IApplicationDbContext db, IObjectReadUrlService signer, ReadAccessOptions options)
    : IRequestHandler<GetFileReadAccessQuery, Result<FileReadAccessResult>>
{
    public async Task<Result<FileReadAccessResult>> Handle(GetFileReadAccessQuery request, CancellationToken ct)
    {
        var context = await contextResolver.ResolveContextAsync(ct);
        if (request.Purpose is not ("download" or "inline" or "stream"))
            return Result<FileReadAccessResult>.Failure("Mục đích đọc không hợp lệ.");
        var grant = await access.CheckAsync(context, request.FileId, PermissionNames.FileRead, ct);
        if (grant is null)
            return Result<FileReadAccessResult>.Failure("Không thể đọc tệp tin.");

        var version = await LoadCurrentVersionAsync(context, grant, ct);
        if (version is null)
            return Result<FileReadAccessResult>.Failure("Không thể đọc tệp tin.");
        var (file, content, provider) = version.Value;
        var contentType = SafeContentType(content.ContentType);
        if (request.Purpose == "inline" && !CanInline(contentType)
            || request.Purpose == "stream" && !CanStream(contentType))
            return Result<FileReadAccessResult>.Failure("Định dạng không hỗ trợ mục đích đọc này.");
        if (provider.Type != StorageProviderType.S3)
            return Result<FileReadAccessResult>.Failure("Kho lưu trữ không hỗ trợ URL đọc trực tiếp.");

        var read = await signer.CreateAsync(new ReadUrlRequest(provider.Id, content.ObjectKey,
            file.Name, contentType, request.Purpose, options.Lifetime), ct);
        if (request.Purpose == "inline" && (request.ApiOrigin is null
            || read.Url.GetLeftPart(UriPartial.Authority) == request.ApiOrigin.GetLeftPart(UriPartial.Authority)
            || read.Url.Scheme != "https" && !read.Url.IsLoopback))
            return Result<FileReadAccessResult>.Failure("Origin đọc inline không hợp lệ.");

        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        var latest = await LoadCurrentVersionAsync(context, grant, ct);
        if (latest is null || latest.Value.Content.ObjectKey != content.ObjectKey
            || latest.Value.Content.ProviderId != content.ProviderId
            || latest.Value.Content.ContentType != content.ContentType
            || latest.Value.Content.SizeBytes != content.SizeBytes
            || latest.Value.File.Name != file.Name)
            return Result<FileReadAccessResult>.Failure("Tệp tin đã thay đổi.");

        db.AuditLogs.Add(new AuditLog
        {
            TenantId = context.TenantId,
            UserId = context.Actor.UserId,
            Action = "FileReadAccessIssued",
            EntityName = "FileEntry",
            EntityId = file.Id,
            Details = $"Purpose={request.Purpose};DocumentVersion={grant.DocumentVersion};ProviderId={provider.Id}"
        });
        await db.SaveChangesAsync(ct);
        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        return Result<FileReadAccessResult>.Success(new(read.Url, read.ExpiresAt,
            grant.DocumentVersion, contentType, content.SizeBytes, read.SupportsRange));
    }

    private async Task<(FileEntry File, FileContentVersion Content, StorageProvider Provider)?>
        LoadCurrentVersionAsync(FileAccessContext context, FileAccessGrant grant, CancellationToken ct)
    {
        var file = await db.FileEntries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(f => f.Id == grant.FileId
            && f.TenantId == context.TenantId && !f.IsDeleted && f.Lifecycle == FileLifecycle.Active
            && f.DocumentVersion == grant.DocumentVersion, ct);
        if (file is null) return null;
        var content = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v =>
            v.TenantId == context.TenantId && v.FileId == file.Id
            && v.DocumentVersion == grant.DocumentVersion && v.ScanStatus == FileScanStatus.Clean
            && v.ObjectKey != "", ct);
        if (content is null) return null;
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p =>
            p.Id == content.ProviderId && p.IsActive, ct);
        return provider is null ? null : (file, content, provider);
    }

    private static string SafeContentType(string value)
    {
        var mime = value.Split(';', 2)[0].Trim().ToLowerInvariant();
        return mime.Length > 0 && mime.Length <= 100 && mime.All(c => c is >= 'a' and <= 'z'
            or >= '0' and <= '9' or '/' or '-' or '+' or '.') ? mime : "application/octet-stream";
    }

    private static bool CanInline(string mime) => mime is "application/pdf" or "image/png"
        or "image/jpeg" or "image/gif" or "image/webp" or "text/plain";
    private static bool CanStream(string mime) => mime is "audio/mpeg" or "audio/mp4"
        or "audio/ogg" or "audio/wav" or "video/mp4" or "video/webm";
}
