using CleanArchCqrs.Application.Common.Models.Files;
namespace CleanArchCqrs.Application.Common.Interfaces;

public sealed record PreviewResult(string Status, long DocumentVersion, string? Reason = null,
    Uri? Url = null, DateTimeOffset? ExpiresAt = null, string? ContentType = null, bool SupportsRange = false);
public interface IPreviewProvider
{
    Task<PreviewResult> GetAsync(FileAccessContext context, FileAccessGrant grant, bool enqueue, CancellationToken ct);
}
// Persist the exact immutable key before uploading so purge/retry can find even an interrupted write.
public interface IPrivateArtifactStorage
{
    Task PutPrivateArtifactAsync(string key, Stream content, CancellationToken ct);
}
public sealed record FileTextCursor(Guid UserId, Guid SessionId, Guid TenantId, Guid FileId,
    long DocumentVersion, Guid GenerationId, string ModelVersion, int Ordinal, DateTimeOffset ExpiresAt);
public interface IFileTextCursorProtector
{
    string Protect(FileTextCursor cursor);
    FileTextCursor Unprotect(string value);
}
public sealed record FileTextOptions(string ModelVersion);
