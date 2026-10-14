using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;

/// <summary>
/// DTO đại diện cho một thành viên/nhân viên trong phòng ban, bao gồm thông tin tài khoản và hạn mức lưu trữ.
/// </summary>
public class DepartmentMemberDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }

    public List<string> Roles { get; set; } = new();

    public Guid DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? DepartmentCode { get; set; }

    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }

    // Thông tin hạn mức dung lượng (Quota)
    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public long StorageAvailableBytes => Math.Max(0, StorageQuotaBytes - StorageUsedBytes);
    public double UsagePercentage => StorageQuotaBytes > 0
        ? Math.Round((double)StorageUsedBytes / StorageQuotaBytes * 100, 2)
        : 0;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static DepartmentMemberDto Create(User user, UserStorageQuota? quota = null)
    {
        var roles = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role.Name)
            .Distinct()
            .ToList();

        var quotaBytes = quota?.QuotaBytes ?? 0L;
        var usedBytes = quota?.UsedBytes ?? 0L;

        return new DepartmentMemberDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            Roles = roles,
            DepartmentId = user.DepartmentId ?? Guid.Empty,
            DepartmentName = user.Department?.Name,
            DepartmentCode = user.Department?.Code,
            TenantId = user.TenantId,
            TenantName = user.Tenant?.Name,
            StorageQuotaBytes = quotaBytes,
            StorageUsedBytes = usedBytes,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}
