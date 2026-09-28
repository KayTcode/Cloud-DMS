using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Persistence;

/// <summary>
/// Handles database initialization and seeding of default Tenants, Departments, Permissions, Roles, and System Administrator.
/// </summary>
public static class DbInitializer
{
    // Fixed GUID constants for consistent testing
    public static readonly Guid FptTenantId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid FptItDeptId = new("22222222-2222-2222-2222-222222222221");
    public static readonly Guid FptHrDeptId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid FptFinDeptId = new("22222222-2222-2222-2222-222222222223");

    public static readonly Guid VngTenantId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid VngGameDeptId = new("44444444-4444-4444-4444-444444444441");

    public static async Task SeedDefaultDataAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger logger)
    {
        try
        {
            await SeedTenantsAsync(context, logger);
            await SeedDepartmentsAsync(context, logger);
            await SeedPermissionsAsync(context, logger);
            await SeedRolesAsync(context, logger);
            await SeedRolePermissionsAsync(context, logger);
            await SeedSystemAdminAsync(context, passwordHasher, logger);

            logger.LogInformation("Database seed completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database seed failed.");
            throw;
        }
    }

    // ============================================================
    // 1. TENANTS SEEDING
    // ============================================================
    private static async Task SeedTenantsAsync(AppDbContext context, ILogger logger)
    {
        var tenants = new[]
        {
            new Tenant
            {
                Id = FptTenantId,
                Code = "FPT",
                Name = "FPT Corporation",
                Description = "FPT Technology & Cloud Enterprise",
                StorageQuotaBytes = 50L * 1024 * 1024 * 1024, // 50 GB
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tenant
            {
                Id = VngTenantId,
                Code = "VNG",
                Name = "VNG Corporation",
                Description = "VNG Digital & Games Enterprise",
                StorageQuotaBytes = 100L * 1024 * 1024 * 1024, // 100 GB
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        foreach (var tenant in tenants)
        {
            var exists = await context.Tenants.AnyAsync(t => t.Id == tenant.Id || t.Code == tenant.Code);
            if (!exists)
            {
                await context.Tenants.AddAsync(tenant);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Tenants seeded successfully.");
    }

    // ============================================================
    // 2. DEPARTMENTS SEEDING
    // ============================================================
    private static async Task SeedDepartmentsAsync(AppDbContext context, ILogger logger)
    {
        var departments = new[]
        {
            // FPT Departments
            new Department
            {
                Id = FptItDeptId,
                TenantId = FptTenantId,
                Code = "IT",
                Name = "Information Technology",
                Description = "Software Engineering & Infrastructure",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Department
            {
                Id = FptHrDeptId,
                TenantId = FptTenantId,
                Code = "HR",
                Name = "Human Resources",
                Description = "Talent Acquisition & People Ops",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Department
            {
                Id = FptFinDeptId,
                TenantId = FptTenantId,
                Code = "FIN",
                Name = "Finance & Accounting",
                Description = "Financial Management",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            // VNG Departments
            new Department
            {
                Id = VngGameDeptId,
                TenantId = VngTenantId,
                Code = "GAME",
                Name = "Game Studio & Publishing",
                Description = "Game Development & Operations",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        foreach (var dept in departments)
        {
            var exists = await context.Departments.AnyAsync(d => d.Id == dept.Id || (d.TenantId == dept.TenantId && d.Code == dept.Code));
            if (!exists)
            {
                await context.Departments.AddAsync(dept);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Departments seeded successfully.");
    }

    // ============================================================
    // 3. PERMISSIONS SEEDING
    // ============================================================
    private static async Task SeedPermissionsAsync(AppDbContext context, ILogger logger)
    {
        var permissionNames = new[]
        {
            "tenant.read",
            "tenant.manage",

            "user.read",
            "user.create",
            "user.update",
            "user.delete",

            "role.read",
            "role.manage",

            "file.read",
            "file.upload",
            "file.update",
            "file.delete",
            "file.share",

            "folder.read",
            "folder.create",
            "folder.update",
            "folder.delete",

            "storage.read",
            "storage.manage",

            "audit.read"
        };

        var existingPermissions = await context.Permissions
            .Select(p => p.Name)
            .ToListAsync();

        var missingPermissions = permissionNames
            .Where(name => !existingPermissions.Contains(name))
            .Select(name => new Permission
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = $"Permission to perform {name} action.",
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingPermissions.Count > 0)
        {
            await context.Permissions.AddRangeAsync(missingPermissions);
            await context.SaveChangesAsync();
            logger.LogInformation("Permissions seeded successfully.");
        }
    }

    // ============================================================
    // 4. ROLES SEEDING
    // ============================================================
    private static async Task SeedRolesAsync(AppDbContext context, ILogger logger)
    {
        var defaultRoles = new (string Name, string Description)[]
        {
            ("SystemAdmin", "System Administrator with full access across all tenants"),
            ("TenantOwner", "Tenant Owner with full administrative control within tenant"),
            ("TenantAdmin", "Tenant Administrator managing users and resources within tenant"),
            ("DepartmentAdmin", "Department Administrator managing department files and users"),
            ("User", "Standard enterprise user")
        };

        var existingRoles = await context.Roles
            .Where(r => r.TenantId == null)
            .Select(r => r.Name)
            .ToListAsync();

        var missingRoles = defaultRoles
            .Where(r => !existingRoles.Contains(r.Name))
            .Select(r => new Role
            {
                Id = Guid.NewGuid(),
                TenantId = null,
                Name = r.Name,
                Description = r.Description,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingRoles.Count > 0)
        {
            await context.Roles.AddRangeAsync(missingRoles);
            await context.SaveChangesAsync();
            logger.LogInformation("Roles seeded successfully.");
        }
    }

    // ============================================================
    // 5. ROLE PERMISSIONS SEEDING (Full permissions for SystemAdmin)
    // ============================================================
    private static async Task SeedRolePermissionsAsync(AppDbContext context, ILogger logger)
    {
        var systemAdminRole = await context.Roles
            .FirstOrDefaultAsync(r => r.Name == "SystemAdmin" && r.TenantId == null);

        if (systemAdminRole == null)
        {
            return;
        }

        var allPermissions = await context.Permissions.ToListAsync();
        var existingRolePermissions = await context.RolePermissions
            .Where(rp => rp.RoleId == systemAdminRole.Id)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var missingRolePermissions = allPermissions
            .Where(p => !existingRolePermissions.Contains(p.Id))
            .Select(p => new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = systemAdminRole.Id,
                PermissionId = p.Id,
                Role = systemAdminRole,
                Permission = p,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingRolePermissions.Count > 0)
        {
            await context.RolePermissions.AddRangeAsync(missingRolePermissions);
            await context.SaveChangesAsync();
            logger.LogInformation("SystemAdmin permissions seeded successfully.");
        }
    }

    // ============================================================
    // 6. SYSTEM ADMIN ACCOUNT SEEDING
    // ============================================================
    private static async Task SeedSystemAdminAsync(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        ILogger logger)
    {
        const string adminEmail = "admin@system.local";

        var existingUser = await context.Users
            .FirstOrDefaultAsync(u => u.Email == adminEmail);

        if (existingUser != null)
        {
            return;
        }

        var systemAdminRole = await context.Roles
            .FirstOrDefaultAsync(r => r.Name == "SystemAdmin" && r.TenantId == null);

        if (systemAdminRole == null)
        {
            throw new InvalidOperationException("SystemAdmin role must be seeded before creating SystemAdmin user.");
        }

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            FirstName = "System",
            LastName = "Administrator",
            PhoneNumber = "0123456789",
            PasswordHash = passwordHasher.HashPassword("Admin@123"),
            IsActive = true,
            TenantId = null,
            DepartmentId = null,
            CreatedAt = DateTime.UtcNow
        };

        adminUser.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = adminUser.Id,
            RoleId = systemAdminRole.Id,
            User = adminUser,
            Role = systemAdminRole,
            CreatedAt = DateTime.UtcNow
        });

        await context.Users.AddAsync(adminUser);
        await context.SaveChangesAsync();

        logger.LogInformation("SystemAdmin user created: {Email} (Default Password: Admin@123)", adminEmail);
    }
}
