using System.Linq.Expressions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core DbContext for the application.
/// </summary>
public class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUserService? _currentUserService;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IDateTimeProvider dateTimeProvider,
        ICurrentUserService? currentUserService = null)
        : base(options)
    {
        _dateTimeProvider = dateTimeProvider;
        _currentUserService = currentUserService;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<FileEntry> FileEntries => Set<FileEntry>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<CleanArchCqrs.Domain.Entities.FileShare> FileShares => Set<CleanArchCqrs.Domain.Entities.FileShare>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<UserStorageQuota> UserStorageQuotas => Set<UserStorageQuota>();
    public DbSet<AuthorizationOperation> AuthorizationOperations => Set<AuthorizationOperation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuthorizationScope> AuthorizationScopes => Set<AuthorizationScope>();
    public DbSet<QuotaReservation> QuotaReservations => Set<QuotaReservation>();
    public DbSet<StorageProvider> StorageProviders => Set<StorageProvider>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditableEntities();

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ApplyTenantScope(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    private void ApplyTenantScope(ModelBuilder modelBuilder)
    {
        var currentRole = _currentUserService?.Role;
        if (!string.IsNullOrWhiteSpace(currentRole) && currentRole.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var currentTenantId = _currentUserService?.TenantId;
        if (!currentTenantId.HasValue)
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tenantProperty = entityType.FindProperty("TenantId");
            if (tenantProperty == null || tenantProperty.PropertyInfo == null)
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var property = Expression.Property(parameter, tenantProperty.PropertyInfo);
            Expression predicate;

            if (tenantProperty.ClrType == typeof(Guid?))
            {
                var nullCheck = Expression.Equal(property, Expression.Constant(null, typeof(Guid?)));
                var matchCheck = Expression.Equal(property, Expression.Constant(currentTenantId.Value, typeof(Guid?)));
                predicate = Expression.OrElse(nullCheck, matchCheck);
            }
            else
            {
                predicate = Expression.Equal(property, Expression.Constant(currentTenantId.Value, tenantProperty.ClrType));
            }

            var lambda = Expression.Lambda(predicate, parameter);
            entityType.SetQueryFilter(lambda);
        }
    }

    private void UpdateAuditableEntities()
    {
        var entries = ChangeTracker
            .Entries<BaseEntity>()
            .Where(entry =>
                entry.State == EntityState.Added ||
                entry.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue =
                    _dateTimeProvider.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue =
                    _dateTimeProvider.UtcNow;
            }
        }
    }
}