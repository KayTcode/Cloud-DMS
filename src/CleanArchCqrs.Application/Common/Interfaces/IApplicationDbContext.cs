using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<Department> Departments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<FileEntry> FileEntries { get; }
    DbSet<Folder> Folders { get; }
    DbSet<CleanArchCqrs.Domain.Entities.FileShare> FileShares { get; }
    DbSet<EmailVerificationToken> EmailVerificationTokens { get; }
    DbSet<UserStorageQuota> UserStorageQuotas { get; }
    DbSet<AuthorizationOperation> AuthorizationOperations { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AuthorizationScope> AuthorizationScopes { get; }
    DbSet<QuotaReservation> QuotaReservations { get; }
    DbSet<StorageProvider> StorageProviders { get; }

    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
