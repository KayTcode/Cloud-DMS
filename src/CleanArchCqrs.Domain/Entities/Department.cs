using CleanArchCqrs.Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Represents a department inside an enterprise tenant (e.g., IT, HR, Accounting).
/// </summary>
public class Department : BaseEntity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Unique code within tenant (e.g., IT, HR)
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
}
