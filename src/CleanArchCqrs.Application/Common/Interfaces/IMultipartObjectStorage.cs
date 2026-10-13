namespace CleanArchCqrs.Application.Common.Interfaces;

public sealed record MultipartObjectInfo(long SizeBytes, string ContentType);
public sealed record MultipartPart(int Number, string ETag);
public sealed record IncompleteMultipartUpload(string Key, string UploadId, DateTimeOffset StartedAt);

// A service instance owns its provider client and must be disposed by the caller.
public interface IMultipartObjectStorage : IDisposable
{
    Task<string> BeginAsync(string key, string contentType, CancellationToken cancellationToken);
    Task<Uri> PresignPartAsync(string key, string uploadId, int partNumber,
        TimeSpan lifetime, CancellationToken cancellationToken);
    Task CompleteAsync(string key, string uploadId, IReadOnlyList<MultipartPart> parts,
        CancellationToken cancellationToken);
    Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken);
    Task<MultipartObjectInfo?> InspectAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<IncompleteMultipartUpload>> ListIncompleteAsync(
        DateTimeOffset startedBefore, CancellationToken cancellationToken);
}
