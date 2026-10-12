using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Entities;

public class AuthorizationScope : BaseEntity
{
    public string ScopeKey { get; set; } = string.Empty;
    public long Version { get; set; }
    public bool IsUpdating { get; set; }
    public Guid? ActiveOperationId { get; set; }
}
