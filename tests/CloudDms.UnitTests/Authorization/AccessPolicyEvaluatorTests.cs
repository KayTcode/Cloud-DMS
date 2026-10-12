using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Domain.Enums;

namespace CloudDms.UnitTests.Authorization;

public sealed class AccessPolicyEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    private readonly AccessPolicyEvaluator _evaluator = new();

    [Fact]
    public void AdminRoleDoesNotGrantFileContentAccess()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TenantAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionNames.FileRead },
            ownerId: Guid.NewGuid());

        var result = _evaluator.Evaluate(input, PermissionNames.FileRead);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    [Fact]
    public void ReadOnlyShareDoesNotGrantWrite()
    {
        var input = CreateInput(
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionNames.FileRead,
                PermissionNames.FileUpdate
            },
            ownerId: Guid.NewGuid(),
            grants: new[]
            {
                new ShareGrant(UserId, CanRead: true, CanWrite: false, CanDelete: false, ExpiresAt: null)
            });

        var result = _evaluator.Evaluate(input, PermissionNames.FileUpdate);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("SystemAdmin")]
    public void CrossTenantAccessIsDenied(string role)
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role },
            resourceTenantId: Guid.NewGuid());

        var result = _evaluator.Evaluate(input, PermissionNames.FileRead);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    [Fact]
    public void ExpiredShareIsDenied()
    {
        var input = CreateInput(
            ownerId: Guid.NewGuid(),
            grants: new[]
            {
                new ShareGrant(UserId, CanRead: true, CanWrite: true, CanDelete: false, ExpiresAt: Now)
            });

        var result = _evaluator.Evaluate(input, PermissionNames.FileRead);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("DepartmentAdmin")]
    public void SameTenantShareReadAllowsReadOnlyForUserAndDepartmentAdmin(string role)
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role },
            ownerId: Guid.NewGuid(),
            grants: new[]
            {
                new ShareGrant(UserId, CanRead: true, CanWrite: false, CanDelete: false, ExpiresAt: null)
            });

        var result = _evaluator.Evaluate(input, PermissionNames.FileRead);

        Assert.Equal(AuthorizationDecision.Allow, result.Decision);
    }

    [Fact]
    public void SystemAdminDoesNotGainTenantBinPurgeAccess()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SystemAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionNames.BinPurge },
            lifecycle: FileLifecycle.Binned);

        var result = _evaluator.Evaluate(input, PermissionNames.BinPurge);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    [Fact]
    public void IncompleteRedisSnapshotIsUnavailable()
    {
        var input = CreateInput(snapshotsComplete: false);

        var result = _evaluator.Evaluate(input, PermissionNames.FileRead);

        Assert.Equal(AuthorizationDecision.Unavailable, result.Decision);
    }

    [Fact]
    public void TenantAdminCannotManageUserInAnotherTenant()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TenantAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionNames.UserManage },
            resourceTenantId: Guid.NewGuid());

        var result = _evaluator.Evaluate(input, PermissionNames.UserManage);

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    [Fact]
    public void SystemAdminMayManageUserInAnotherTenant()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SystemAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionNames.UserManage },
            resourceTenantId: Guid.NewGuid());

        var result = _evaluator.Evaluate(input, PermissionNames.UserManage);

        Assert.Equal(AuthorizationDecision.Allow, result.Decision);
    }

    [Fact]
    public void TenantAdminCanReadTenantMetadataWithoutContentAccess()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TenantAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionNames.FileMetadataRead
            });

        Assert.Equal(
            AuthorizationDecision.Allow,
            _evaluator.Evaluate(input, PermissionNames.FileMetadataRead).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(input, PermissionNames.FileRead).Decision);
    }

    [Fact]
    public void TenantAdminCanPurgeOnlyBinnedFiles()
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PermissionNames.BinPurge
        };
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TenantAdmin" };
        var binnedFile = CreateInput(
            roles: roles,
            permissions: permissions,
            lifecycle: FileLifecycle.Binned);
        var activeFile = CreateInput(roles: roles, permissions: permissions);

        Assert.Equal(
            AuthorizationDecision.Allow,
            _evaluator.Evaluate(binnedFile, PermissionNames.BinPurge).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(activeFile, PermissionNames.BinPurge).Decision);
    }

    [Fact]
    public void DepartmentAdminCanAllocateQuotaOnlyWithinTheirDepartment()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DepartmentAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionNames.UserQuotaAllocate
            });

        Assert.Equal(
            AuthorizationDecision.Allow,
            _evaluator.Evaluate(input, PermissionNames.UserQuotaAllocate).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(
                CreateInput(
                    roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DepartmentAdmin" },
                    permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        PermissionNames.UserQuotaAllocate
                    },
                    resourceDepartmentId: Guid.NewGuid()),
                PermissionNames.UserQuotaAllocate).Decision);
    }

    [Fact]
    public void DepartmentAdminCanManageUsersButNotDepartmentSettingsOrDepartmentQuota()
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DepartmentAdmin" };
        var input = CreateInput(
            roles: roles,
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionNames.UserManage,
                PermissionNames.DepartmentManage,
                PermissionNames.DepartmentQuotaAllocate
            });

        Assert.Equal(
            AuthorizationDecision.Allow,
            _evaluator.Evaluate(input, PermissionNames.UserManage).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(input, PermissionNames.DepartmentManage).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(input, PermissionNames.DepartmentQuotaAllocate).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(
                CreateInput(
                    roles: roles,
                    permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        PermissionNames.UserManage
                    },
                    resourceDepartmentId: Guid.NewGuid()),
                PermissionNames.UserManage).Decision);
    }

    [Fact]
    public void TenantAdminCanAllocateDepartmentQuotaOnlyInTheirTenant()
    {
        var input = CreateInput(
            roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TenantAdmin" },
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionNames.DepartmentQuotaAllocate
            });

        Assert.Equal(
            AuthorizationDecision.Allow,
            _evaluator.Evaluate(input, PermissionNames.DepartmentQuotaAllocate).Decision);
        Assert.Equal(
            AuthorizationDecision.Deny,
            _evaluator.Evaluate(
                CreateInput(
                    roles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SystemAdmin" },
                    permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        PermissionNames.DepartmentQuotaAllocate
                    }),
                PermissionNames.DepartmentQuotaAllocate).Decision);
    }

    [Fact]
    public void UnknownActionFailsClosedEvenWhenPermissionIsPresent()
    {
        var input = CreateInput(
            permissions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "file.unrecognized" });

        var result = _evaluator.Evaluate(input, "file.unrecognized");

        Assert.Equal(AuthorizationDecision.Deny, result.Decision);
    }

    private static AccessEvaluationInput CreateInput(
        HashSet<string>? roles = null,
        HashSet<string>? permissions = null,
        Guid? ownerId = null,
        Guid? resourceTenantId = null,
        IReadOnlyList<ShareGrant>? grants = null,
        bool snapshotsComplete = true,
        FileLifecycle lifecycle = FileLifecycle.Active,
        Guid? resourceDepartmentId = null)
    {
        var user = new UserAccessSnapshot(
            UserId,
            TenantId,
            Guid.NewGuid(),
            IsActive: true,
            Version: 1,
            roles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "User" },
            permissions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionNames.FileRead });
        var session = new SessionAccessSnapshot(
            SessionId,
            UserId,
            IsActive: true,
            ExpiresAt: Now.AddHours(1),
            Version: 1,
            Complete: true);
        var resource = new ResourceAccessSnapshot(
            ResourceId,
            resourceTenantId ?? TenantId,
            resourceDepartmentId ?? user.DepartmentId,
            ownerId ?? UserId,
            lifecycle,
            Version: 1);

        return new AccessEvaluationInput(
            user,
            session,
            resource,
            grants ?? Array.Empty<ShareGrant>(),
            Scopes: new[]
            {
                new ScopeAccessSnapshot(
                    $"dms:scope:department:{Guid.NewGuid():N}",
                    Version: 1,
                    IsUpdating: false,
                    Complete: true)
            },
            SnapshotsComplete: snapshotsComplete,
            ServerTime: Now,
            Generation: "test-generation",
            Target: new AuthorizationTarget("file", ResourceId));
    }
}
