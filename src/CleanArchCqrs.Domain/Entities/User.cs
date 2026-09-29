using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

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

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public User() { }

    public User(Guid? tenantId, Guid? departmentId, string email, string passwordHash, string firstName, string lastName, string? phoneNumber)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        DepartmentId = departmentId;
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
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
