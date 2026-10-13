using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class FileAclQuery(AppDbContext db) : IFileAclQuery
{
    public const int BatchSize = 256;

    public async Task<CurrentFileAcl?> ResolveFolderAsync(
        FileAccessContext context, Guid folderId, DateTimeOffset now, CancellationToken ct)
    {
        var graph = await LoadGraphAsync(context, now, ct);
        if (!graph.Folders.TryGetValue(folderId, out var folder))
            return null;
        var ancestors = Ancestors(folderId, graph.Folders);
        if (ancestors is null)
            return null;
        var grants = graph.Grants.Where(g => g.ResourceKind == ResourceKind.Folder
            && ancestors.Any(a => a.Id == g.ResourceId)).Select(g => ToShare(context, g)).ToArray();
        return new(new(folder.Id, folder.TenantId, folder.DepartmentId, Guid.Empty, FileLifecycle.Active,
            folder.AuthorizationVersion), 0, false, grants, Fingerprint(Guid.Empty, folder.Id, ancestors, grants));
    }

    public async Task<IReadOnlyDictionary<Guid, CurrentFileAcl>> ResolveManyAsync(
        FileAccessContext context, IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var graph = await LoadGraphAsync(context, now, ct);
        var result = new Dictionary<Guid, CurrentFileAcl>();
        foreach (var chunk in ids.Distinct().Chunk(BatchSize))
        {
            var files = await db.FileEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(f => f.TenantId == context.TenantId && chunk.Contains(f.Id)).ToArrayAsync(ct);
            var versions = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
                .Where(v => v.TenantId == context.TenantId && chunk.Contains(v.FileId))
                .Select(v => new { v.FileId, v.DocumentVersion, v.ScanStatus }).ToArrayAsync(ct);
            var shares = await LegacyShares(context).Where(s => chunk.Contains(s.FileEntryId)).ToArrayAsync(ct);
            foreach (var file in files)
            {
                var ancestors = Ancestors(file.FolderId, graph.Folders);
                if (ancestors is null)
                    continue;
                var grants = graph.Grants.Where(g =>
                    g.ResourceKind == ResourceKind.File && g.ResourceId == file.Id
                    || g.ResourceKind == ResourceKind.Folder && ancestors.Any(a => a.Id == g.ResourceId))
                    .Select(g => ToShare(context, g)).ToList();
                grants.AddRange(shares.Where(s => s.FileEntryId == file.Id).Select(s => new ShareGrant(
                    context.Actor.UserId, s.CanRead, s.CanWrite, s.CanDelete,
                    s.ExpiresAt.HasValue
                        ? new DateTimeOffset(DateTime.SpecifyKind(s.ExpiresAt.Value, DateTimeKind.Utc))
                        : null)));
                var fingerprint = Fingerprint(file.OwnerId, file.FolderId, ancestors, grants);
                var lifecycle = file.IsDeleted && file.Lifecycle == FileLifecycle.Active ? FileLifecycle.Binned : file.Lifecycle;
                var clean = versions.Any(v => v.FileId == file.Id && v.DocumentVersion == file.DocumentVersion
                    && v.ScanStatus == FileScanStatus.Clean);
                result[file.Id] = new(new(file.Id, file.TenantId, file.OwningDepartmentId, file.OwnerId,
                    lifecycle, file.AuthorizationVersion), file.DocumentVersion, clean, grants, fingerprint);
            }
        }
        return result;
    }

    public async Task<IQueryable<FileEntry>> ReadScopeAsync(
        FileAccessContext context, DateTimeOffset now, CancellationToken ct)
    {
        var graph = await LoadGraphAsync(context, now, ct);
        var folderIds = graph.Folders.Values.Where(f =>
            (Ancestors(f.Id, graph.Folders) ?? Array.Empty<Folder>()).Any(a =>
                graph.Grants.Any(g => g.ResourceKind == ResourceKind.Folder && g.ResourceId == a.Id)))
            .Select(f => f.Id).ToArray();
        var validFolderIds = graph.Folders.Keys.Where(id => Ancestors(id, graph.Folders) is not null).ToArray();
        var shares = LegacyShares(context);
        var fileIds = graph.Grants.Where(g => g.ResourceKind == ResourceKind.File).Select(g => g.ResourceId).ToArray();
        var actor = context.Actor.UserId;
        var tenant = context.TenantId;
        var utc = now.UtcDateTime;
        return db.FileEntries.IgnoreQueryFilters().AsNoTracking().Where(f =>
            f.TenantId == tenant && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted
            && (!f.FolderId.HasValue || validFolderIds.Contains(f.FolderId.Value))
            && db.FileContentVersions.IgnoreQueryFilters().Any(v => v.TenantId == tenant
                && v.FileId == f.Id && v.DocumentVersion == f.DocumentVersion && v.ScanStatus == FileScanStatus.Clean)
            && (f.OwnerId == actor || fileIds.Contains(f.Id)
                || f.FolderId.HasValue && folderIds.Contains(f.FolderId.Value)
                || shares.Any(s => s.FileEntryId == f.Id && s.CanRead && (!s.ExpiresAt.HasValue || s.ExpiresAt > utc))));
    }

    private IQueryable<CleanArchCqrs.Domain.Entities.FileShare> LegacyShares(FileAccessContext context) =>
        from share in db.FileShares.IgnoreQueryFilters().AsNoTracking()
        join file in db.FileEntries.IgnoreQueryFilters().AsNoTracking() on share.FileEntryId equals file.Id
        join user in db.Users.IgnoreQueryFilters().AsNoTracking() on share.SharedWithUserId equals user.Id
        where file.TenantId == context.TenantId && user.TenantId == context.TenantId && user.Id == context.Actor.UserId
        select share;

    private async Task<Graph> LoadGraphAsync(FileAccessContext context, DateTimeOffset now, CancellationToken ct)
    {
        var tenant = context.TenantId;
        var actor = context.Actor.UserId;
        var folders = await db.Folders.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.TenantId == tenant).ToDictionaryAsync(f => f.Id, ct);
        var grants = await db.ResourceGrants.IgnoreQueryFilters().AsNoTracking().Where(g =>
            g.TenantId == tenant && (g.Role == ResourceRole.Viewer || g.Role == ResourceRole.Editor || g.Role == ResourceRole.Owner)
            && (!g.ExpiresAt.HasValue || g.ExpiresAt > now)
            && (g.PrincipalKind == PrincipalKind.User && g.PrincipalId == actor
                || g.PrincipalKind == PrincipalKind.Team
                && db.Teams.IgnoreQueryFilters().Any(t => t.TenantId == tenant && t.Id == g.PrincipalId)
                && db.TeamMembers.IgnoreQueryFilters().Any(m => m.TenantId == tenant
                    && m.TeamId == g.PrincipalId && m.UserId == actor))).ToArrayAsync(ct);
        return new(folders, grants);
    }

    private static IReadOnlyList<Folder>? Ancestors(Guid? folderId, IReadOnlyDictionary<Guid, Folder> folders)
    {
        var result = new List<Folder>();
        var seen = new HashSet<Guid>();
        while (folderId.HasValue)
        {
            if (!seen.Add(folderId.Value) || !folders.TryGetValue(folderId.Value, out var folder))
                return null;
            result.Add(folder);
            folderId = folder.ParentFolderId;
        }
        return result;
    }

    private static ShareGrant ToShare(FileAccessContext context, ResourceGrant grant) =>
        new(context.Actor.UserId, true, grant.Role is ResourceRole.Editor or ResourceRole.Owner,
            grant.Role == ResourceRole.Owner, grant.ExpiresAt, grant.Role == ResourceRole.Owner);

    private static string Fingerprint(Guid ownerId, Guid? folderId, IReadOnlyList<Folder> ancestors,
        IReadOnlyList<ShareGrant> grants) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                OwnerId = ownerId, FolderId = folderId,
                Folders = ancestors.Select(a => new { a.Id, a.ParentFolderId, a.AuthorizationVersion }),
                Grants = grants.OrderBy(g => JsonSerializer.Serialize(g))
            }))));

    private sealed record Graph(IReadOnlyDictionary<Guid, Folder> Folders, IReadOnlyList<ResourceGrant> Grants);
}
