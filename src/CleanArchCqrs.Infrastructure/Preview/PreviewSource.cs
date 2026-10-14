using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Infrastructure.Preview;
internal static class PreviewSource
{
    public static Task<FileContentVersion?> LoadAsync(AppDbContext db, Guid tenant, Guid file, long version, CancellationToken ct) =>
        (from f in db.FileEntries.IgnoreQueryFilters().AsNoTracking()
         join v in db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
            on new { f.TenantId, FileId = f.Id, f.DocumentVersion } equals new { v.TenantId, v.FileId, v.DocumentVersion }
         where f.Id == file && f.TenantId == tenant && f.DocumentVersion == version
            && f.Lifecycle == FileLifecycle.Active && !f.IsDeleted && f.ScanStatus == "Clean"
            && v.ScanStatus == FileScanStatus.Clean && v.PhysicalStorageDeletedAt == null
            && v.ObjectKey == f.StorageKey && v.ProviderId == f.StorageProviderId && v.Hash == f.Hash && v.Hash.Length == 64
         select v).SingleOrDefaultAsync(ct);
    public static bool Same(FileContentVersion? current, FileContentVersion original) => current is not null
        && current.Id == original.Id && current.Hash == original.Hash && current.ObjectKey == original.ObjectKey
        && current.ProviderId == original.ProviderId && current.SizeBytes == original.SizeBytes
        && current.ContentType == original.ContentType;
}
