using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public class LocalStorageService : IStorageService, IIntentObjectStorage
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

        var fullTargetDir = Path.GetFullPath(targetDir);
        var fullBasePath = Path.GetFullPath(_basePath);
        if (!fullTargetDir.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn lưu trữ thư mục không hợp lệ.");
        }

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Chống Path Traversal: Chỉ lấy tên file thuần và loại bỏ ký tự không hợp lệ
        var safeName = Path.GetFileName(fileName);
        safeName = string.Join("_", safeName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "unnamed_file";
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{safeName}";
        var filePath = Path.Combine(targetDir, uniqueFileName);

        var fullFilePath = Path.GetFullPath(filePath);
        if (!fullFilePath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn tệp tin không hợp lệ.");
        }

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return Path.GetRelativePath(_basePath, filePath).Replace('\\', '/');
    }

    public async Task<string> UploadForIntentAsync(Stream stream, Guid intentId, string contentType,
        string? parentFolderId, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_basePath, "legacy");
        Directory.CreateDirectory(directory);
        var key = $"legacy/{intentId:N}";
        await using var output = new FileStream(Path.Combine(directory, intentId.ToString("N")),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await stream.CopyToAsync(output, cancellationToken);
        return key;
    }

    public Task<IReadOnlyList<string>> FindIntentObjectsAsync(Guid intentId,
        string? parentFolderId, CancellationToken cancellationToken)
    {
        var key = $"legacy/{intentId:N}";
        IReadOnlyList<string> result = File.Exists(Path.Combine(_basePath, "legacy", intentId.ToString("N")))
            ? new[] { key } : Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_basePath, storageKey);
        var fullFilePath = Path.GetFullPath(filePath);
        var fullBasePath = Path.GetFullPath(_basePath);
        if (!fullFilePath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn tệp tin không hợp lệ.");
        }

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
        var fullFilePath = Path.GetFullPath(filePath);
        var fullBasePath = Path.GetFullPath(_basePath);
        if (!fullFilePath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn tệp tin không hợp lệ.");
        }

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
