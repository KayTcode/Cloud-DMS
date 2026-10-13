using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Models.Files;
namespace CleanArchCqrs.Application.Common.Interfaces;
public interface IFileAccessService
{
    Task<IReadOnlyDictionary<Guid, FileAccessGrant>> CheckManyAsync(FileAccessContext context, IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken);
    Task<FileAccessGrant?> CheckAsync(FileAccessContext context, Guid fileId, string action, CancellationToken cancellationToken);
    Task AssertStillValidAsync(FileAccessContext context, IReadOnlyCollection<FileAccessGrant> grants, CancellationToken cancellationToken);
}
