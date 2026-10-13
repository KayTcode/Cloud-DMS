using System.Text.Json;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Infrastructure.Security;
internal sealed class AuthorizationProjectionBuilder(AppDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal async Task<IReadOnlyList<ScopeProjection>> BuildScopeProjectionsAsync(
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var scopeKind = scopeKey.Split(':', 2)[0];
        Guid? scopeId = scopeKind == "global"
            ? null
            : Guid.Parse(scopeKey[(scopeKey.IndexOf(':') + 1)..]);

        var usersQuery = dbContext.Users.AsNoTracking().AsQueryable();
        var filesQuery = dbContext.FileEntries
            .AsNoTracking()
            .Include(file => file.Shares)
            .AsQueryable();

        switch (scopeKind)
        {
            case "global":
                break;
            case "tenant":
                usersQuery = usersQuery.Where(user => user.TenantId == scopeId);
                filesQuery = filesQuery.Where(file => file.TenantId == scopeId);
                break;
            case "department":
                usersQuery = usersQuery.Where(user => user.DepartmentId == scopeId);
                filesQuery = filesQuery.Where(file => file.OwningDepartmentId == scopeId);
                break;
            default:
                throw new ArgumentException("The authorization scope kind is invalid.", nameof(scopeKey));
        }

        var projections = new List<ScopeProjection>();
        var users = await usersQuery.ToListAsync(cancellationToken);
        var userIds = users.Select(user => user.Id).ToArray();
        var roleRows = await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userIds.Contains(userRole.UserId))
            .Select(userRole => new { userRole.UserId, Name = userRole.Role.Name })
            .ToListAsync(cancellationToken);
        var permissionRows = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join rolePermission in dbContext.RolePermissions.AsNoTracking()
                on userRole.RoleId equals rolePermission.RoleId
            join permission in dbContext.Permissions.AsNoTracking()
                on rolePermission.PermissionId equals permission.Id
            where userIds.Contains(userRole.UserId)
            select new { userRole.UserId, Name = permission.Name })
            .ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            var roles = roleRows
                .Where(row => row.UserId == user.Id)
                .Select(row => row.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var permissions = permissionRows
                .Where(row => row.UserId == user.Id)
                .Select(row => row.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var snapshot = new UserProjection(
                user.Id,
                user.TenantId,
                user.DepartmentId,
                user.IsActive,
                user.AuthorizationVersion,
                roles,
                permissions,
                Complete: true);
            projections.Add(new ScopeProjection(
                $"dms:user:{user.Id:N}".ToLowerInvariant(),
                JsonSerializer.Serialize(snapshot, JsonOptions)));
        }

        var sessions = await dbContext.RefreshTokens.AsNoTracking()
            .Where(session => userIds.Contains(session.UserId)).ToListAsync(cancellationToken);
        foreach (var session in sessions)
            projections.Add(new ScopeProjection($"auth:session:{session.Id:N}", JsonSerializer.Serialize(new
            {
                sessionId = session.Id, userId = session.UserId, isActive = session.IsActive,
                blocked = session.RevokedAt.HasValue,
                expiresAtUnixMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                version = session.RevokedAt.HasValue ? 2 : 1, complete = true
            }, JsonOptions)));

        var files = await filesQuery.ToListAsync(cancellationToken);
        foreach (var file in files)
        {
            var snapshot = new FileProjection(
                file.Id,
                file.TenantId,
                file.OwningDepartmentId,
                file.OwnerId,
                file.Lifecycle.ToString(),
                file.AuthorizationVersion,
                Complete: true);
            projections.Add(new ScopeProjection(
                $"dms:file:{file.Id:N}".ToLowerInvariant(),
                JsonSerializer.Serialize(snapshot, JsonOptions)));

            foreach (var share in file.Shares)
            {
                var grant = new GrantProjection(
                    share.SharedWithUserId,
                    share.CanRead,
                    share.CanWrite,
                    share.CanDelete,
                    share.ExpiresAt.HasValue
                        ? new DateTimeOffset(DateTime.SpecifyKind(share.ExpiresAt.Value, DateTimeKind.Utc))
                            .ToUnixTimeMilliseconds()
                        : null,
                    file.AuthorizationVersion,
                    Complete: true);
                projections.Add(new ScopeProjection(
                    $"dms:grant:{file.Id:N}:{share.SharedWithUserId:N}".ToLowerInvariant(),
                    JsonSerializer.Serialize(grant, JsonOptions)));
            }
        }

        return projections;
    }

    internal sealed record ScopeProjection(string Key, string Json);

    private sealed record UserProjection(
        Guid UserId,
        Guid? TenantId,
        Guid? DepartmentId,
        bool IsActive,
        long Version,
        string[] Roles,
        string[] Permissions,
        bool Complete);

    private sealed record FileProjection(
        Guid Id,
        Guid TenantId,
        Guid? DepartmentId,
        Guid OwnerId,
        string Lifecycle,
        long Version,
        bool Complete);

    private sealed record GrantProjection(
        Guid UserId,
        bool CanRead,
        bool CanWrite,
        bool CanDelete,
        long? ExpiresAtUnixMilliseconds,
        long ResourceVersion,
        bool Complete);
}
