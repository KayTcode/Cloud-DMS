using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public string? ReplacedByTokenHash { get; private set; }

    public string? CreatedByIp { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsActive =>
        RevokedAt == null && ExpiresAt > DateTime.UtcNow;
}
