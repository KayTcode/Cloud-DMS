using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public User User { get; set; } = null!;

    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;

    public RefreshToken() { }

    public RefreshToken(Guid id, Guid userId, string tokenHash, DateTime expiresAt, string? createdByIp) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public void Revoke(string? replacedByTokenHash = null)
    {
        if (RevokedAt.HasValue) return;

        RevokedAt = DateTime.UtcNow;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
