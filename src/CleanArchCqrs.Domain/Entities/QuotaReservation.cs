using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Domain.Entities;

public class QuotaReservation : BaseEntity
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid TenantId { get; set; }
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public QuotaReservationState State { get; set; } = QuotaReservationState.Pending;

    public User User { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}
