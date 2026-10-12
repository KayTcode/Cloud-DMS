using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Features.TenantAdmin.DTOs;

public class DepartmentDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int HeadCount { get; set; }
    public string Lead { get; set; } = "Chưa chỉ định";
    public string? AdminEmail { get; set; }
    public long StorageQuotaBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public double AllocatedStorageGB => StorageQuotaBytes > 0 
        ? Math.Round((double)StorageQuotaBytes / (1024 * 1024 * 1024), 2) 
        : 0;
    public double UsedStorageGB => StorageUsedBytes > 0 
        ? Math.Round((double)StorageUsedBytes / (1024 * 1024 * 1024), 2) 
        : 0;
    public DateTime CreatedAt { get; set; }

    public static DepartmentDto FromEntity(Department dept)
    {
        // Find DepartmentAdmin user if any
        var adminUser = dept.Users
            .FirstOrDefault(u => u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == "DepartmentAdmin"));

        var leadName = adminUser != null 
            ? adminUser.FullName 
            : (dept.Users.FirstOrDefault()?.FullName ?? "Chưa chỉ định");

        return new DepartmentDto
        {
            Id = dept.Id,
            TenantId = dept.TenantId,
            Name = dept.Name,
            Code = dept.Code,
            Description = dept.Description,
            IsActive = dept.IsActive,
            HeadCount = dept.Users.Count,
            Lead = leadName,
            AdminEmail = adminUser?.Email,
            StorageQuotaBytes = dept.StorageQuotaBytes,
            StorageUsedBytes = dept.StorageUsedBytes,
            CreatedAt = dept.CreatedAt
        };
    }
}
