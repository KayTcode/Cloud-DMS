using CleanArchCqrs.Application.Common.Interfaces;

namespace CloudDms.IntegrationTests.Support;

public sealed class StorageProbe : IStorageService
{
    public int UploadCalls { get; private set; }
    public int DownloadCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public Exception? Failure { get; set; }

    public Task<string> UploadFileAsync(
        Stream stream,
        string fileName,
        string contentType,
        string? parentFolderId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfiguredToFail();
        UploadCalls++;
        return Task.FromResult($"test-storage/{Guid.NewGuid():N}");
    }

    public Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ThrowIfConfiguredToFail();
        DownloadCalls++;
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task<bool> DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        ThrowIfConfiguredToFail();
        DeleteCalls++;
        return Task.FromResult(true);
    }

    public Task<StorageQuotaDto> GetProviderQuotaAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfConfiguredToFail();
        return Task.FromResult(new StorageQuotaDto(1024, 0, 1024));
    }

    public Task<string> CreateFolderAsync(
        string folderName,
        string? parentFolderId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfiguredToFail();
        return Task.FromResult($"test-folder/{Guid.NewGuid():N}");
    }

    private void ThrowIfConfiguredToFail()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
