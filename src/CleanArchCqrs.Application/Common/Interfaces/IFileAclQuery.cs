using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
namespace CleanArchCqrs.Application.Common.Interfaces;
public sealed record CurrentFileAcl(ResourceAccessSnapshot Resource, long DocumentVersion, bool VerifiedClean, IReadOnlyList<ShareGrant> Grants, string Fingerprint);
public interface IFileAclQuery
{
    Task<CurrentFileAcl?> ResolveFolderAsync(FileAccessContext context, Guid folderId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, CurrentFileAcl>> ResolveManyAsync(FileAccessContext context, IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken cancellationToken);
    // Candidate scope only. Consumer MUST perform CheckMany/AssertStillValid before returning results.
    Task<IQueryable<FileEntry>> ReadScopeAsync(FileAccessContext context, DateTimeOffset now, CancellationToken cancellationToken);
}
