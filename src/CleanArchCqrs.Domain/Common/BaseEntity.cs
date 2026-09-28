namespace CleanArchCqrs.Domain.Common;

/// <summary>
/// Base entity with ID and audit timestamps - all domain entities inherit from this.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
