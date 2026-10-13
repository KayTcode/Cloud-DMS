using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Commands.UploadFile;

public sealed class UploadQuotaExceededException(string message) : InvalidOperationException(message);

// All counter writers use one ordered SQL delta protocol, including shared providers.
public sealed class UploadQuota(IApplicationDbContext db)
{
    public async Task ReserveStagingAsync(Guid providerId, long bytes, long limit, CancellationToken ct)
    {
        if (bytes <= 0 || limit < bytes) throw new UploadQuotaExceededException("Temporary storage limit exceeded.");
        var updated = await db.StorageProviders.IgnoreQueryFilters()
            .Where(p => p.Id == providerId && p.StagingUsedBytes <= limit - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StagingUsedBytes,
                p => p.StagingUsedBytes + bytes), ct);
        if (updated != 1) throw new UploadQuotaExceededException("Temporary storage limit exceeded.");
    }

    public async Task ReleaseStagingAsync(Guid providerId, long bytes, CancellationToken ct)
    {
        if (bytes <= 0) return;
        await db.StorageProviders.IgnoreQueryFilters().Where(p => p.Id == providerId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StagingUsedBytes,
                p => p.StagingUsedBytes >= bytes ? p.StagingUsedBytes - bytes : 0), ct);
    }

    public async Task AdjustStagingAsync(Guid providerId, long delta, long limit, CancellationToken ct)
    {
        if (delta > 0)
        {
            if (limit < delta || await db.StorageProviders.IgnoreQueryFilters()
                .Where(p => p.Id == providerId && p.StagingUsedBytes <= limit - delta)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StagingUsedBytes,
                    p => p.StagingUsedBytes + delta), ct) != 1)
                throw new UploadQuotaExceededException("Temporary storage limit exceeded.");
        }
        else if (delta < 0)
            await ReleaseStagingAsync(providerId, checked(-delta), ct);
    }

    // Call in a SQL transaction after deleting the immutable object; the CAS and
    // provider counter release then commit together, so retries cannot double-refund.
    public async Task<bool> RecordPhysicalCleanupAsync(Guid contentVersionId, CancellationToken ct)
    {
        var version = await db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == contentVersionId, ct);
        if (version is null || version.RetainedStagingBytes <= 0) return false;
        var updated = await db.FileContentVersions.IgnoreQueryFilters()
            .Where(v => v.Id == contentVersionId && v.ProviderId == version.ProviderId
                && v.RetainedStagingBytes == version.RetainedStagingBytes
                && v.PhysicalStorageDeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.RetainedStagingBytes, 0L)
                .SetProperty(v => v.PhysicalStorageDeletedAt, DateTimeOffset.UtcNow), ct);
        if (updated != 1) return false;
        await ReleaseStagingAsync(version.ProviderId, version.RetainedStagingBytes, ct);
        return true;
    }

    public async Task ReserveAsync(Guid tenantId, Guid? departmentId, Guid userId,
        Guid providerId, long bytes, CancellationToken ct, long? providerBytes = null)
    {
        var providerCharge = providerBytes ?? bytes;
        if (bytes < 0 || providerCharge < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes > 0 && await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId
            && t.StorageQuotaBytes >= bytes && t.StorageUsedBytes <= t.StorageQuotaBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes,
                t => t.StorageUsedBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("Tenant storage quota exceeded.");
        if (bytes > 0 && departmentId.HasValue && await db.Departments.IgnoreQueryFilters()
            .Where(d => d.Id == departmentId && d.TenantId == tenantId && d.QuotaBytes >= bytes
                && d.UsedBytes <= d.QuotaBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedBytes, d => d.UsedBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("Department storage quota exceeded.");
        if (bytes > 0 && await db.UserStorageQuotas.IgnoreQueryFilters().Where(q => q.UserId == userId
            && q.QuotaBytes >= bytes && q.UsedBytes <= q.QuotaBytes - bytes)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes, q => q.UsedBytes + bytes), ct) != 1)
            throw new UploadQuotaExceededException("User storage quota exceeded.");
        if (providerCharge > 0 && await db.StorageProviders.IgnoreQueryFilters().Where(p => p.Id == providerId
            && p.TotalCapacityBytes >= providerCharge
            && p.UsedCapacityBytes <= p.TotalCapacityBytes - providerCharge)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCapacityBytes,
                p => p.UsedCapacityBytes + providerCharge), ct) != 1)
            throw new UploadQuotaExceededException("Provider storage quota exceeded.");
    }

    public async Task ReleaseAsync(Guid tenantId, Guid? departmentId, Guid userId,
        Guid providerId, long bytes, CancellationToken ct, long? providerBytes = null)
    {
        var providerRefund = providerBytes ?? bytes;
        if (bytes < 0 || providerRefund < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes > 0) await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StorageUsedBytes,
                t => t.StorageUsedBytes >= bytes ? t.StorageUsedBytes - bytes : 0), ct);
        if (bytes > 0 && departmentId.HasValue)
            await db.Departments.IgnoreQueryFilters().Where(d => d.Id == departmentId && d.TenantId == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UsedBytes,
                    d => d.UsedBytes >= bytes ? d.UsedBytes - bytes : 0), ct);
        if (bytes > 0) await db.UserStorageQuotas.IgnoreQueryFilters().Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.UsedBytes,
                q => q.UsedBytes >= bytes ? q.UsedBytes - bytes : 0), ct);
        if (providerRefund > 0) await db.StorageProviders.IgnoreQueryFilters().Where(p => p.Id == providerId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCapacityBytes,
                p => p.UsedCapacityBytes >= providerRefund ? p.UsedCapacityBytes - providerRefund : 0), ct);
    }
}
