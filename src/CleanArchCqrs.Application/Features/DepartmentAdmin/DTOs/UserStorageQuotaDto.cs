namespace CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;

public class UserStorageQuotaDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;

    public long QuotaBytes { get; set; }
    public long UsedBytes { get; set; }
    public int FileCount { get; set; }
    public long AvailableBytes => Math.Max(0, QuotaBytes - UsedBytes);
    public double UsagePercentage => QuotaBytes > 0 
        ? Math.Round((double)UsedBytes / QuotaBytes * 100, 2) 
        : 0;
}


public class DepartmentUsersQuotaSummaryDto
{
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public long DepartmentQuotaBytes { get; set; }
    public long DepartmentUsedBytes { get; set; }
    public long TotalAllocatedToUsersBytes { get; set; }
    public long UnallocatedDepartmentQuotaBytes => Math.Max(0, DepartmentQuotaBytes - TotalAllocatedToUsersBytes);

    public List<UserStorageQuotaDto> Users { get; set; } = new();
}
