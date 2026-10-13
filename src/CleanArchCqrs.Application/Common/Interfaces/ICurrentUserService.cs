namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? SessionId { get; }
    Guid? TenantId { get; }
    Guid? DepartmentId { get; }
    string? Email { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
