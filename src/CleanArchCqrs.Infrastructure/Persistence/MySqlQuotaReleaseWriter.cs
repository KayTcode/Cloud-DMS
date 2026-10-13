using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Persistence;

public sealed class MySqlQuotaReleaseWriter(AppDbContext db) : IQuotaReleaseWriter
{
    public async Task ReleaseAsync(FileEntry file, CancellationToken ct)
    {
        await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == file.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes,
                t => t.StorageUsedBytes > file.SizeBytes ? t.StorageUsedBytes - file.SizeBytes : 0), ct);

        var departmentId = file.OwningDepartmentId ?? file.DepartmentId;
        if (!departmentId.HasValue)
            departmentId = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == file.TenantId && u.Id == file.OwnerId)
                .Select(u => u.DepartmentId).SingleOrDefaultAsync(ct);
        if (departmentId.HasValue)
        {
            await db.Departments.IgnoreQueryFilters().Where(d => d.TenantId == file.TenantId && d.Id == departmentId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedBytes,
                    d => d.UsedBytes > file.SizeBytes ? d.UsedBytes - file.SizeBytes : 0), ct);
        }

        var quotaId = await db.UserStorageQuotas.IgnoreQueryFilters()
            .Where(q => q.UserId == file.OwnerId && db.Users.IgnoreQueryFilters()
                .Any(u => u.Id == q.UserId && u.TenantId == file.TenantId))
            .Select(q => q.Id).SingleOrDefaultAsync(ct);
        if (quotaId != Guid.Empty)
        {
            await db.UserStorageQuotas.IgnoreQueryFilters().Where(q => q.Id == quotaId)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes,
                    q => q.UsedBytes > file.SizeBytes ? q.UsedBytes - file.SizeBytes : 0), ct);
        }

        // Provider capacity is shared across tenants; the file's provider ID is authoritative.
        if (file.StorageProviderId.HasValue)
        {
            await db.StorageProviders.IgnoreQueryFilters().Where(p => p.Id == file.StorageProviderId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCapacityBytes,
                    p => p.UsedCapacityBytes > file.SizeBytes ? p.UsedCapacityBytes - file.SizeBytes : 0), ct);
        }
    }
}
