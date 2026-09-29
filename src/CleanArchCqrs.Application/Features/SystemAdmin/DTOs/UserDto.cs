using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Features.SystemAdmin.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }

    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();

    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? TenantCode { get; set; }

    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? DepartmentCode { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public static UserDto FromEntity(User user)
    {
        var roles = user.UserRoles
            .Where(ur => ur.Role != null)
            .Select(ur => ur.Role.Name)
            .Distinct()
            .ToList();

        var permissions = user.UserRoles
            .Where(ur => ur.Role != null && ur.Role.RolePermissions != null)
            .SelectMany(ur => ur.Role.RolePermissions)
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToList();

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            Roles = roles,
            Permissions = permissions,
            TenantId = user.TenantId,
            TenantName = user.Tenant?.Name,
            TenantCode = user.Tenant?.Code,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            DepartmentCode = user.Department?.Code,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}
