namespace CleanArchCqrs.Application.Features.TenantAdmin.DTOs;

public class DepartmentStorageQuotaDto
{
    public Guid DepartmentId { get; set; }
    public Guid TenantId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string DepartmentCode { get; set; } = string.Empty;

    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public long AvailableBytes => Math.Max(0, StorageQuotaBytes - StorageUsedBytes);
    public double UsagePercentage => StorageQuotaBytes > 0 
        ? Math.Round((double)StorageUsedBytes / StorageQuotaBytes * 100, 2) 
        : 0;

    public long TotalAllocatedToUsersBytes { get; set; }
    public long UnallocatedBytes => Math.Max(0, StorageQuotaBytes - TotalAllocatedToUsersBytes);
}
