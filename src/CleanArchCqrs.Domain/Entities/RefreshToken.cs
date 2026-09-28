using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
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
        private RefreshToken() { }

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
}
