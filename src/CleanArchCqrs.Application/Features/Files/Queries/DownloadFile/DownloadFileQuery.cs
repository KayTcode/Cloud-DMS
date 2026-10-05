using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.DownloadFile;

public record DownloadFileResult(
    Stream Stream,
    string FileName,
    string ContentType);

public record DownloadFileQuery(Guid FileId) : IRequest<Result<DownloadFileResult>>;

public class DownloadFileQueryHandler : IRequestHandler<DownloadFileQuery, Result<DownloadFileResult>>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageServiceFactory _storageServiceFactory;

    public DownloadFileQueryHandler(
        IApplicationDbContext context,
        IStorageServiceFactory storageServiceFactory)
    {
        _context = context;
        _storageServiceFactory = storageServiceFactory;
    }

    public async Task<Result<DownloadFileResult>> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        var fileEntry = await _context.FileEntries
            .Include(f => f.StorageProvider)
            .FirstOrDefaultAsync(f => f.Id == request.FileId && !f.IsDeleted, cancellationToken);

        if (fileEntry == null)
        {
            return Result<DownloadFileResult>.Failure("Tệp tin không tồn tại hoặc đã bị xóa.");
        }

        if (fileEntry.StorageProvider == null)
        {
            return Result<DownloadFileResult>.Failure("Không tìm thấy thông tin cấu hình nhà cung cấp lưu trữ của tệp tin.");
        }

        var storageService = _storageServiceFactory.CreateService(fileEntry.StorageProvider);
        try
        {
            var stream = await storageService.DownloadFileAsync(fileEntry.StorageKey, cancellationToken);
            return Result<DownloadFileResult>.Success(new DownloadFileResult(stream, fileEntry.Name, fileEntry.ContentType));
        }
        catch (Exception ex)
        {
            return Result<DownloadFileResult>.Failure($"Không thể tải tệp tin từ kho lưu trữ: {ex.Message}");
        }
    }
}
