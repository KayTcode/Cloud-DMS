using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public sealed class UploadQuotaExceededException(string message) : InvalidOperationException(message);

// All counter writers use one ordered SQL delta protocol, including shared providers.
public sealed class UploadQuota(IApplicationDbContext db)
{
    public async Task ReserveAsync(Guid tenantId, Guid? departmentId, Guid userId,
        Guid providerId, long bytes, CancellationToken ct)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId
            && t.StorageQuotaBytes >= bytes && t.StorageUsedBytes <= t.StorageQuotaBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes,
                t => t.StorageUsedBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("Tenant storage quota exceeded.");
        if (departmentId.HasValue && await db.Departments.IgnoreQueryFilters()
            .Where(d => d.Id == departmentId && d.TenantId == tenantId && d.QuotaBytes >= bytes
                && d.UsedBytes <= d.QuotaBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedBytes, d => d.UsedBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("Department storage quota exceeded.");
        if (await db.UserStorageQuotas.IgnoreQueryFilters().Where(q => q.UserId == userId
            && q.QuotaBytes >= bytes && q.UsedBytes <= q.QuotaBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes, q => q.UsedBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("User storage quota exceeded.");
        if (await db.StorageProviders.IgnoreQueryFilters().Where(p => p.Id == providerId
            && p.TotalCapacityBytes >= bytes && p.UsedCapacityBytes <= p.TotalCapacityBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCapacityBytes,
                p => p.UsedCapacityBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("Provider storage quota exceeded.");
    }

    public async Task ReleaseAsync(Guid tenantId, Guid? departmentId, Guid userId,
        Guid providerId, long bytes, CancellationToken ct)
    {
        if (bytes <= 0) return;
        await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes,
                t => t.StorageUsedBytes >= bytes ? t.StorageUsedBytes - bytes : 0), ct);
        if (departmentId.HasValue)
            await db.Departments.IgnoreQueryFilters().Where(d => d.Id == departmentId && d.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedBytes,
                    d => d.UsedBytes >= bytes ? d.UsedBytes - bytes : 0), ct);
        await db.UserStorageQuotas.IgnoreQueryFilters().Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes,
                q => q.UsedBytes >= bytes ? q.UsedBytes - bytes : 0), ct);
        await db.StorageProviders.IgnoreQueryFilters().Where(p => p.Id == providerId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCapacityBytes,
                p => p.UsedCapacityBytes >= bytes ? p.UsedCapacityBytes - bytes : 0), ct);
    }
}
