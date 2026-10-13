namespace CleanArchCqrs.Application.Common.Authorization;

public sealed record ActorIdentity(Guid UserId, Guid SessionId);

public sealed record AuthorizationTarget(string Kind, Guid? Id);

public enum AuthorizationDecision
{
    Allow,
    Deny,
    Unavailable,
    Unauthenticated
}

public sealed record AuthorizationStamp(
    string Generation,
    string Action,
    Guid SessionId,
    long UserVersion,
    long SessionVersion,
    IReadOnlyList<ScopeVersionStamp> ScopeVersions,
    AuthorizationTarget Target,
    long ResourceVersion);

public sealed record ScopeVersionStamp(string Key, long Version);

public sealed record AuthorizationResult(
    AuthorizationDecision Decision,
    AuthorizationStamp? Stamp = null);

public sealed record UserAccessSnapshot(
    Guid UserId,
    Guid? TenantId,
    Guid? DepartmentId,
    bool IsActive,
    long Version,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions);

public sealed record ResourceAccessSnapshot(
    Guid Id,
    Guid? TenantId,
    Guid? DepartmentId,
    Guid OwnerId,
    CleanArchCqrs.Domain.Enums.FileLifecycle Lifecycle,
    long Version);

public sealed record ShareGrant(
    Guid UserId,
    bool CanRead,
    bool CanWrite,
    bool CanDelete,
    DateTimeOffset? ExpiresAt,
    bool CanShare = false);

public sealed record SessionAccessSnapshot(
    Guid SessionId,
    Guid UserId,
    bool IsActive,
    DateTimeOffset ExpiresAt,
    long Version,
    bool Complete);

public sealed record ScopeAccessSnapshot(
    string Key,
    long Version,
    bool IsUpdating,
    bool Complete);

public sealed record AccessEvaluationInput(
    UserAccessSnapshot? User,
    SessionAccessSnapshot? Session,
    ResourceAccessSnapshot? Resource,
    IReadOnlyList<ShareGrant> Grants,
    IReadOnlyList<ScopeAccessSnapshot> Scopes,
    bool SnapshotsComplete,
    DateTimeOffset ServerTime,
    string Generation,
    AuthorizationTarget Target);
