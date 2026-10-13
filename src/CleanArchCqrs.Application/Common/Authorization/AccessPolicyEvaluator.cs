using CleanArchCqrs.Domain.Enums;

namespace CleanArchCqrs.Application.Common.Authorization;

public sealed class AccessPolicyEvaluator
{
    private const string SystemAdminRole = "SystemAdmin";
    private const string TenantAdminRole = "TenantAdmin";
    private const string DepartmentAdminRole = "DepartmentAdmin";

    public AuthorizationResult Evaluate(AccessEvaluationInput input, string action)
    {
        if (!input.SnapshotsComplete
            || input.Scopes.Count == 0
            || input.Scopes.Any(scope => !scope.Complete || scope.IsUpdating)
            || string.IsNullOrWhiteSpace(input.Generation))
        {
            return new AuthorizationResult(AuthorizationDecision.Unavailable);
        }

        if (input.User is null || input.Session is null || !input.Session.Complete)
        {
            return new AuthorizationResult(AuthorizationDecision.Unavailable);
        }

        if (!input.Session.IsActive
            || input.Session.ExpiresAt <= input.ServerTime
            || input.Session.UserId != input.User.UserId)
        {
            return new AuthorizationResult(AuthorizationDecision.Unauthenticated);
        }

        if (!input.User.IsActive)
        {
            return new AuthorizationResult(AuthorizationDecision.Deny);
        }

        var resource = input.Resource;
        if (!IsSystemAction(action) && resource is null && action.StartsWith("file.", StringComparison.Ordinal))
        {
            return Deny(input, action);
        }

        if (resource is not null
            && !IsSystemAction(action)
            && (!resource.TenantId.HasValue
                || !input.User.TenantId.HasValue
                || (input.User.TenantId != resource.TenantId
                    && (!IsSystemAdmin(input.User) || action.StartsWith("file.", StringComparison.Ordinal)))))
        {
            return Deny(input, action);
        }

        var owner = input.Resource?.OwnerId == input.User.UserId;
        var grants = input.Grants.Where(candidate => candidate.UserId == input.User.UserId
            && (!candidate.ExpiresAt.HasValue || candidate.ExpiresAt.Value > input.ServerTime)).ToArray();

        var allowed = action switch
        {
            PermissionNames.FileRead => CanAccessContent(input, resource, action, owner || grants.Any(grant => grant.CanRead)),
            PermissionNames.FileUpdate => CanAccessContent(input, resource, action, owner || grants.Any(grant => grant.CanWrite)),
            PermissionNames.FileDelete => CanAccessContent(input, resource, action, owner || grants.Any(grant => grant.CanDelete)),
            PermissionNames.FileShare => CanAccessContent(input, resource, action, owner || grants.Any(grant => grant.CanShare)),
            PermissionNames.FileRestore or PermissionNames.FilePurge => resource is { Lifecycle: FileLifecycle.Binned }
                && (owner || grants.Any(grant => grant.CanDelete)) && HasPermission(input.User, PermissionNames.FileDelete),
            PermissionNames.FileMetadataRead => IsTenantAdmin(input.User) && HasPermission(input.User, action),
            PermissionNames.BinPurge => HasRole(input.User, TenantAdminRole)
                && resource is { Lifecycle: FileLifecycle.Binned }
                && HasPermission(input.User, action),
            PermissionNames.UserManage => CanManageTenantUsers(input, resource, action),
            PermissionNames.DepartmentManage => CanManageDepartment(input, resource, action),
            PermissionNames.DepartmentQuotaAllocate => CanAllocateDepartmentQuota(input, resource, action),
            PermissionNames.UserQuotaAllocate => CanAllocateUserQuota(input, resource, action),
            PermissionNames.AuditRead => CanReadAudit(input, resource, action),
            PermissionNames.SystemManage => IsSystemAdmin(input.User) && HasPermission(input.User, action),
            _ => false
        };

        return allowed ? Allow(input, action) : Deny(input, action);
    }

    private static bool CanAccessContent(
        AccessEvaluationInput input,
        ResourceAccessSnapshot? resource,
        string action,
        bool relationAllows)
    {
        return resource is { Lifecycle: FileLifecycle.Active }
            && relationAllows
            && HasPermission(input.User!, action);
    }

    private static bool CanManageTenantUsers(
        AccessEvaluationInput input,
        ResourceAccessSnapshot? target,
        string action)
    {
        if (!HasPermission(input.User!, action) || target is null)
        {
            return false;
        }

        return IsSystemAdmin(input.User!)
            || (IsTenantAdmin(input.User!)
                && input.User!.TenantId.HasValue
                && target.TenantId == input.User.TenantId)
            || (action == PermissionNames.UserManage
                && HasRole(input.User!, DepartmentAdminRole)
                && input.User!.DepartmentId.HasValue
                && target.DepartmentId == input.User.DepartmentId);
    }

    private static bool CanManageDepartment(
        AccessEvaluationInput input,
        ResourceAccessSnapshot? target,
        string action)
    {
        if (!HasPermission(input.User!, action) || target is null)
        {
            return false;
        }

        return IsSystemAdmin(input.User!)
            || (IsTenantAdmin(input.User!)
                && input.User!.TenantId.HasValue
                && target.TenantId == input.User.TenantId);
    }

    private static bool CanAllocateDepartmentQuota(
        AccessEvaluationInput input,
        ResourceAccessSnapshot? target,
        string action) =>
        target is not null
        && IsTenantAdmin(input.User!)
        && input.User!.TenantId.HasValue
        && target.TenantId == input.User.TenantId
        && HasPermission(input.User, action);

    private static bool CanAllocateUserQuota(
        AccessEvaluationInput input,
        ResourceAccessSnapshot? target,
        string action)
    {
        return target is not null
            && HasRole(input.User!, DepartmentAdminRole)
            && input.User!.DepartmentId.HasValue
            && target.DepartmentId == input.User.DepartmentId
            && HasPermission(input.User, action);
    }

    private static bool CanReadAudit(
        AccessEvaluationInput input,
        ResourceAccessSnapshot? target,
        string action)
    {
        if (!HasPermission(input.User!, action))
        {
            return false;
        }

        return IsSystemAdmin(input.User!)
            || (IsTenantAdmin(input.User!)
                && input.User!.TenantId.HasValue
                && target?.TenantId == input.User.TenantId);
    }

    private static bool IsSystemAction(string action) =>
        action is PermissionNames.SystemManage or PermissionNames.AuditRead;

    private static bool IsSystemAdmin(UserAccessSnapshot user) => HasRole(user, SystemAdminRole);

    private static bool IsTenantAdmin(UserAccessSnapshot user) =>
        HasRole(user, TenantAdminRole);

    private static bool HasRole(UserAccessSnapshot user, string role) =>
        user.Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    private static bool HasPermission(UserAccessSnapshot user, string permission) =>
        user.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    private static AuthorizationResult Allow(AccessEvaluationInput input, string action) =>
        new(AuthorizationDecision.Allow, CreateStamp(input, action));

    private static AuthorizationResult Deny(AccessEvaluationInput input, string action) =>
        new(AuthorizationDecision.Deny, CreateStamp(input, action));

    private static AuthorizationStamp CreateStamp(AccessEvaluationInput input, string action) =>
        new(
            input.Generation,
            action,
            input.Session!.SessionId,
            input.User!.Version,
            input.Session.Version,
            input.Scopes
                .OrderBy(scope => scope.Key, StringComparer.Ordinal)
                .Select(scope => new ScopeVersionStamp(scope.Key, scope.Version))
                .ToArray(),
            input.Target,
            input.Resource?.Version ?? 0);
}
