namespace CleanArchCqrs.Domain.Common;

/// <summary>
/// Base entity with ID - all domain entities inherit from this.
/// In the full Patreon version, this includes additional audit fields and domain event handling.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; }

    public DateTime CreatedAt { get; protected set; }

    public DateTime? UpdatedAt { get; protected set; }

    protected BaseEntity()
    {
    }

    protected BaseEntity(Guid id)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
    }

    protected void SetUpdatedAt()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
