namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IIntentObjectStorage
{
    Task<string> UploadForIntentAsync(Stream stream, Guid intentId, string contentType,
        string? parentFolderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> FindIntentObjectsAsync(Guid intentId,
        string? parentFolderId, CancellationToken cancellationToken);
}
