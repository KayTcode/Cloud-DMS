using CleanArchCqrs.Domain.Common;

namespace Domain.Entities;

public class User : BaseEntity
{
    // If SystemAdmin, TenantId & DepartmentId are null
    public Guid? TenantId { get; private set; }
    public Tenant? Tenant { get; private set; }

    // If TenantAdmin, DepartmentId can be null
    public Guid? DepartmentId { get; private set; }
    public Department? Department { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime? LastLoginAt { get; private set; }

    public ICollection<UserRole> UserRoles { get; private set; }
        = new List<UserRole>();

    public ICollection<FileEntry> Files { get; private set; }
        = new List<FileEntry>();

    public ICollection<RefreshToken> RefreshTokens { get; private set; }
        = new List<RefreshToken>();

    public ICollection<AuditLog> AuditLogs { get; private set; }
        = new List<AuditLog>();
    private User() { } // For EF Core
    public User(Guid? tenantId, Guid? departmentId, string email, string passwordHash, string firstName, string lastName, string? phoneNumber)
    {
        TenantId = tenantId;
        DepartmentId = departmentId;
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        IsActive = true;
    }
    public void UpdateProfile(string firstName, string lastName, string? phoneNumber)
    {
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        UpdatedAt = DateTime.UtcNow;
    }
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
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        UpdatedAt = DateTime.UtcNow;
    }
    public void MarkLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
