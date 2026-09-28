using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

/// <summary>
/// Represents a system user across all role tiers.
/// </summary>
public class User : BaseEntity
{
    // If SystemAdmin, TenantId & DepartmentId are null
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    // If TenantAdmin, DepartmentId can be null
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<UserRole> UserRoles { get; set; }
        = new List<UserRole>();

    public ICollection<FileEntry> Files { get; set; }
        = new List<FileEntry>();

    public ICollection<RefreshToken> RefreshTokens { get; set; }
        = new List<RefreshToken>();

    public ICollection<AuditLog> AuditLogs { get; set; }
        = new List<AuditLog>();

    public void Active()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Inactive()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeDepartment(Guid? departmentId)
    {
        DepartmentId = departmentId;
        UpdatedAt = DateTime.UtcNow;
    }
}
