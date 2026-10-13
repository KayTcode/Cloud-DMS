using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Infrastructure.Search;

// The shared ACL query owns grant, inheritance, tenant, lifecycle and scan rules.
internal sealed class FileSearchSqlScope(IFileAclQuery acl)
{
    public async Task<IQueryable<FileEntry>> ReadAsync(FileAccessContext context,
        Guid? folderId, CancellationToken ct)
    {
        var scope = await acl.ReadScopeAsync(context, DateTimeOffset.UtcNow, ct);
        return folderId.HasValue ? scope.Where(file => file.FolderId == folderId.Value) : scope;
    }
}
