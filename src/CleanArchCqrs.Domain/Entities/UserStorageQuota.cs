using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class UserStorageQuota : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid? DepartmentId { get; set; }

    private long _quotaBytes;
    private long _usedBytes;

    public long QuotaBytes
    {
        get => _quotaBytes;
        set => _quotaBytes = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Quota cannot be negative.");
    }

    public long UsedBytes
    {
        get => _usedBytes;
        set => _usedBytes = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Used bytes cannot be negative.");
    }

    public User User { get; set; } = null!;

    public Department? Department { get; set; }
}
