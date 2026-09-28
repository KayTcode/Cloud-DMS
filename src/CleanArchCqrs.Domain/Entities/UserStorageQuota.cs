using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class UserStorageQuota : BaseEntity
{
    public Guid UserId { get; private set; }

    public long QuotaBytes { get; private set; }

    public long UsedBytes { get; private set; }

    public User User { get; private set; } = null!;
}
