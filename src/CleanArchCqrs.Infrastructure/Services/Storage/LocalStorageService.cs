using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public class LocalStorageService : IStorageService
{
    private readonly string _basePath;

    public LocalStorageService(string basePath)
    {
        _basePath = basePath;
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string? parentFolderId = null, CancellationToken cancellationToken = default)
    {
        var targetDir = string.IsNullOrEmpty(parentFolderId) 
            ? _basePath 
            : Path.Combine(_basePath, parentFolderId);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
        var filePath = Path.Combine(targetDir, uniqueFileName);

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return Path.GetRelativePath(_basePath, filePath).Replace('\\', '/');
    }

    public Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_basePath, storageKey);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Local file not found", filePath);
        }

        Stream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(fileStream);
    }

    public Task<bool> DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_basePath, storageKey);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<StorageQuotaDto> GetProviderQuotaAsync(CancellationToken cancellationToken = default)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_basePath)) ?? "C:\\");
        long total = driveInfo.TotalSize;
        long free = driveInfo.AvailableFreeSpace;
        long used = total - free;

        return Task.FromResult(new StorageQuotaDto(total, used, free));
    }

    public Task<string> CreateFolderAsync(string folderName, string? parentFolderId = null, CancellationToken cancellationToken = default)
    {
        var targetDir = string.IsNullOrEmpty(parentFolderId) 
            ? Path.Combine(_basePath, folderName) 
            : Path.Combine(_basePath, parentFolderId, folderName);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        return Task.FromResult(Path.GetRelativePath(_basePath, targetDir).Replace('\\', '/'));
    }
}
