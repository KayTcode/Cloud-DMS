using CleanArchCqrs.Domain.Common;

namespace Domain.Entities;

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
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; private set; }

    public ICollection<UserRole> UserRoles { get; private set; }
        = new List<UserRole>();

    public ICollection<FileEntry> Files { get; private set; }
        = new List<FileEntry>();

    public ICollection<RefreshToken> RefreshTokens { get; private set; }
        = new List<RefreshToken>();

    public ICollection<AuditLog> AuditLogs { get; private set; }
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
