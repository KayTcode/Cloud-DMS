using CleanArchCqrs.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Domain.Entities;

public class UserStorageQuota : BaseEntity
{
    public Guid UserId { get; private set; }

    public long QuotaBytes { get; private set; }

    public long UsedBytes { get; private set; }

    public User User { get; private set; } = null!;
}
