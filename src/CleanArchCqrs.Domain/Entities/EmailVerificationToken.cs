using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class EmailVerificationToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "TenantAdminInvitation"; // e.g. "TenantAdminInvitation", "EmailConfirmation", "PasswordReset"
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;

    public bool IsValid => !IsUsed && ExpiresAt > DateTime.UtcNow;

    public EmailVerificationToken() { }

    public EmailVerificationToken(Guid id, Guid userId, string token, string tokenType = "TenantAdminInvitation", int expirationHours = 48) : base(id)
    {
        UserId = userId;
        Token = token;
        TokenType = tokenType;
        ExpiresAt = DateTime.UtcNow.AddHours(expirationHours);
        IsUsed = false;
    }

    public void MarkAsUsed()
    {
        IsUsed = true;
        UsedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
