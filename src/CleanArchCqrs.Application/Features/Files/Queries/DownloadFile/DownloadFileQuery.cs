using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.DownloadFile;

public record DownloadFileResult(Stream Stream, string FileName, string ContentType);
public record DownloadFileQuery(Guid FileId) : IRequest<Result<DownloadFileResult>>;

public class DownloadFileQueryHandler(
    IApplicationDbContext db, IStorageServiceFactory storageFactory,
    IFileAccessContextResolver contextResolver, IFileAccessService access)
    : IRequestHandler<DownloadFileQuery, Result<DownloadFileResult>>
{
    public async Task<Result<DownloadFileResult>> Handle(DownloadFileQuery request, CancellationToken ct)
    {
        var context = await contextResolver.ResolveContextAsync(ct);
        var grant = await access.CheckAsync(context, request.FileId, PermissionNames.FileRead, ct);
        if (grant is null)
            return Result<DownloadFileResult>.Failure("Không thể đọc tệp tin.");
        var file = await db.FileEntries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(f => f.Id == request.FileId
            && f.TenantId == context.TenantId && f.DocumentVersion == grant.DocumentVersion
            && !f.IsDeleted && f.Lifecycle == FileLifecycle.Active, ct);
        if (file is null)
            return Result<DownloadFileResult>.Failure("Không thể đọc tệp tin.");
        var content = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(v =>
            v.TenantId == context.TenantId && v.FileId == file.Id
            && v.DocumentVersion == grant.DocumentVersion && v.ScanStatus == FileScanStatus.Clean
            && v.ObjectKey != "", ct);
        if (content is null)
            return Result<DownloadFileResult>.Failure("Không thể đọc tệp tin.");
        var provider = await db.StorageProviders.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p =>
            p.Id == content.ProviderId && p.IsActive, ct);
        if (provider is null)
            return Result<DownloadFileResult>.Failure("Kho lưu trữ không khả dụng.");

        Stream stream;
        IStorageService? storageService = null;
        try
        {
            storageService = storageFactory.CreateService(provider);
            stream = await storageService.DownloadFileAsync(content.ObjectKey, ct);
        }
        catch
        {
            (storageService as IDisposable)?.Dispose();
            return Result<DownloadFileResult>.Failure("Không thể tải tệp tin từ kho lưu trữ.");
        }
        try
        {
            await access.AssertStillValidAsync(context, new[] { grant }, ct);
            var current = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking().AnyAsync(v =>
                v.TenantId == context.TenantId && v.FileId == file.Id
                && v.DocumentVersion == grant.DocumentVersion && v.ScanStatus == FileScanStatus.Clean
                && v.ObjectKey == content.ObjectKey && v.ProviderId == provider.Id, ct);
            current = current && await db.StorageProviders.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(p => p.Id == provider.Id && p.IsActive, ct);
            if (!current)
            {
                await stream.DisposeAsync();
                return Result<DownloadFileResult>.Failure("Tệp tin đã thay đổi.");
            }
            return Result<DownloadFileResult>.Success(new(stream, file.Name, content.ContentType));
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }
}
