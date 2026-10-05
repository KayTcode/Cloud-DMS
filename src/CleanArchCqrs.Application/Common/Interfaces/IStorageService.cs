namespace CleanArchCqrs.Application.Common.Interfaces;

public record StorageQuotaDto(
    long TotalCapacityBytes, 
    long UsedCapacityBytes, 
    long AvailableCapacityBytes);

public interface IStorageService
{
    Task<string> UploadFileAsync(
        Stream stream, 
        string fileName, 
        string contentType, 
        string? parentFolderId = null, 
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadFileAsync(
        string storageKey, 
        CancellationToken cancellationToken = default);

    Task<bool> DeleteFileAsync(
        string storageKey, 
        CancellationToken cancellationToken = default);

    Task<StorageQuotaDto> GetProviderQuotaAsync(
        CancellationToken cancellationToken = default);

    Task<string> CreateFolderAsync(
        string folderName, 
        string? parentFolderId = null, 
        CancellationToken cancellationToken = default);
}
