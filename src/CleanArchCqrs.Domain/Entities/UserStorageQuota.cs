using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class UserStorageQuota : BaseEntity
{
    public Guid UserId { get; set; }

    public long QuotaBytes { get; set; }

    public long UsedBytes { get; set; }

    public User User { get; set; } = null!;
}
