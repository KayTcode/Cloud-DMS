using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Infrastructure.Persistence
{
    public static class DbInitializer
    {
        public static async Task SeedDefaultDataAsync(
            AppDbContext context,
            IPasswordHasher passwordHasher,
            ILogger logger)
        {
            try
            {
                await SeedPermissionsAsync(context, logger);
                await SeedRolesAsync(context, logger);
                await SeedRolePermissionsAsync(context, logger);
                await SeedTenantsAndUsersAsync(context, passwordHasher, logger);

                logger.LogInformation(
                    "Default RBAC data & demo accounts seeded successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "An error occurred while seeding default database data.");
            }
        }

        private static async Task SeedPermissionsAsync(
            AppDbContext context,
            ILogger logger)
        {
            var permissions = new Dictionary<string, string>
            {
                ["tenant.read"] = "View tenant information",
                ["tenant.manage"] = "Manage tenant information",

                ["user.read"] = "View users",
                ["user.create"] = "Create users",
                ["user.update"] = "Update users",
                ["user.delete"] = "Delete users",

                ["role.read"] = "View roles",
                ["role.manage"] = "Manage roles",

                ["department.read"] = "View departments",
                ["department.create"] = "Create departments",
                ["department.update"] = "Update departments",
                ["department.delete"] = "Delete departments",

                ["file.read"] = "View files",
                ["file.upload"] = "Upload files",
                ["file.update"] = "Update files",
                ["file.delete"] = "Delete files",
                ["file.share"] = "Share files",

                ["folder.read"] = "View folders",
                ["folder.create"] = "Create folders",
                ["folder.update"] = "Update folders",
                ["folder.delete"] = "Delete folders",

                ["storage.read"] = "View storage information",
                ["storage.manage"] = "Manage storage",

                ["audit.read"] = "View audit logs"
            };

            foreach (var item in permissions)
            {
                var exists = await context.Permissions
                    .AnyAsync(x => x.Name == item.Key);

                if (exists)
                    continue;

                var permission = new Permission(
                    Guid.NewGuid(),
                    item.Key,
                    item.Value);

                await context.Permissions.AddAsync(permission);
            }

            await context.SaveChangesAsync();

            logger.LogInformation(
                "Default permissions seeded.");
        }

        private static async Task SeedRolesAsync(
            AppDbContext context,
            ILogger logger)
        {
            var roles = new Dictionary<string, string>
            {
                ["SystemAdmin"] =
                    "System administrator with full system access",

                ["TenantAdmin"] =
                    "Administrator of a tenant",

                ["DepartmentAdmin"] =
                    "Administrator of a department within a tenant",

                ["Manager"] =
                    "Manager within a tenant",

                ["Employee"] =
                    "Regular employee"
            };

            foreach (var item in roles)
            {
                var exists = await context.Roles
                    .AnyAsync(x => x.Name == item.Key);

                if (exists)
                    continue;

                var role = new Role(
                    Guid.NewGuid(),
                    item.Key,
                    item.Value);

                await context.Roles.AddAsync(role);
            }

            await context.SaveChangesAsync();

            logger.LogInformation(
                "Default roles seeded.");
        }

        private static async Task SeedRolePermissionsAsync(
            AppDbContext context,
            ILogger logger)
        {
            var systemAdmin = await context.Roles
                .FirstAsync(x => x.Name == "SystemAdmin");

            var tenantAdmin = await context.Roles
                .FirstAsync(x => x.Name == "TenantAdmin");

            var departmentAdmin = await context.Roles
                .FirstOrDefaultAsync(x => x.Name == "DepartmentAdmin");

            var manager = await context.Roles
                .FirstAsync(x => x.Name == "Manager");

            var employee = await context.Roles
                .FirstAsync(x => x.Name == "Employee");

            var permissions = await context.Permissions
                .ToDictionaryAsync(x => x.Name);

            var rolePermissions = new Dictionary<string, string[]>
            {
                ["SystemAdmin"] = permissions.Keys.ToArray(),

                ["TenantAdmin"] =
                [
                    "tenant.read",
                    "tenant.manage",

                    "user.read",
                    "user.create",
                    "user.update",
                    "user.delete",

                    "role.read",
                    "role.manage",

                    "department.read",
                    "department.create",
                    "department.update",
                    "department.delete",

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
                ],

                ["DepartmentAdmin"] =
                [
                    "user.read",
                    "user.create",
                    "user.update",

                    "department.read",

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
                ],

                ["Manager"] =
                [
                    "user.read",

                    "department.read",

                    "file.read",
                    "file.upload",
                    "file.update",
                    "file.delete",
                    "file.share",

                    "folder.read",
                    "folder.create",
                    "folder.update",
                    "folder.delete",

                    "storage.read"
                ],

                ["Employee"] =
                [
                    "file.read",
                    "file.upload",
                    "file.update",

                    "folder.read",
                    "folder.create",
                    "folder.update",

                    "storage.read"
                ]
            };

            var roleMap = new Dictionary<string, Role?>
            {
                ["SystemAdmin"] = systemAdmin,
                ["TenantAdmin"] = tenantAdmin,
                ["DepartmentAdmin"] = departmentAdmin,
                ["Manager"] = manager,
                ["Employee"] = employee
            };

            foreach (var rolePermission in rolePermissions)
            {
                if (!roleMap.TryGetValue(rolePermission.Key, out var role) || role == null)
                    continue;

                foreach (var permissionName in rolePermission.Value)
                {
                    if (!permissions.TryGetValue(permissionName, out var permission))
                        continue;

                    var exists = await context.RolePermissions
                        .AnyAsync(x =>
                            x.RoleId == role.Id &&
                            x.PermissionId == permission.Id);

                    if (exists)
                        continue;

                    await context.RolePermissions.AddAsync(
                        new RolePermission(
                            Guid.NewGuid(),
                            role.Id,
                            permission.Id));
                }
            }

            await context.SaveChangesAsync();

            logger.LogInformation(
                "Role permissions seeded.");
        }

        private static async Task SeedTenantsAndUsersAsync(
            AppDbContext context,
            IPasswordHasher passwordHasher,
            ILogger logger)
        {
            // 1. Seed Demo Tenant if none exists
            var acmeTenant = await context.Tenants.FirstOrDefaultAsync(t => t.Code == "ACME");
            if (acmeTenant == null)
            {
                acmeTenant = new Tenant(
                    Guid.NewGuid(),
                    "Acme Corporation",
                    "ACME",
                    100L * 1024 * 1024 * 1024)
                {
                    Description = "Global Enterprise Demo Tenant"
                };
                await context.Tenants.AddAsync(acmeTenant);
                await context.SaveChangesAsync();
            }

            // 2. Seed Demo Department
            var itDept = await context.Departments.FirstOrDefaultAsync(d => d.TenantId == acmeTenant.Id && d.Code == "IT");
            if (itDept == null)
            {
                itDept = new Department(
                    Guid.NewGuid(),
                    acmeTenant.Id,
                    "Information Technology",
                    "IT")
                {
                    Description = "Core Engineering and IT Infrastructure"
                };
                await context.Departments.AddAsync(itDept);
                await context.SaveChangesAsync();
            }

            // Roles
            var sysAdminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "SystemAdmin");
            var tenantAdminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "TenantAdmin");
            var deptAdminRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "DepartmentAdmin") 
                                ?? await context.Roles.FirstOrDefaultAsync(r => r.Name == "Manager");
            var employeeRole = await context.Roles.FirstOrDefaultAsync(r => r.Name == "Employee");

            // 3. Seed System Admin Account
            var sysAdminEmail = "admin@clouddms.com";
            var sysAdminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == sysAdminEmail);
            if (sysAdminUser == null)
            {
                var passwordHash = passwordHasher.Hash("Admin@123");
                sysAdminUser = new User(
                    tenantId: null,
                    departmentId: null,
                    email: sysAdminEmail,
                    passwordHash: passwordHash,
                    firstName: "System",
                    lastName: "Administrator",
                    phoneNumber: "+84 901 234 567"
                );
                await context.Users.AddAsync(sysAdminUser);
                await context.SaveChangesAsync();

                if (sysAdminRole != null)
                {
                    await context.UserRoles.AddAsync(new UserRole(Guid.NewGuid(), sysAdminUser.Id, sysAdminRole.Id));
                    await context.SaveChangesAsync();
                }
            }

            // 4. Seed Tenant Admin Account
            var tenantAdminEmail = "tenant@acme.com";
            var tenantAdminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == tenantAdminEmail);
            if (tenantAdminUser == null)
            {
                var passwordHash = passwordHasher.Hash("Tenant@123");
                tenantAdminUser = new User(
                    tenantId: acmeTenant.Id,
                    departmentId: itDept.Id,
                    email: tenantAdminEmail,
                    passwordHash: passwordHash,
                    firstName: "Alice",
                    lastName: "Manager",
                    phoneNumber: "+84 988 765 432"
                );
                await context.Users.AddAsync(tenantAdminUser);
                await context.SaveChangesAsync();

                if (tenantAdminRole != null)
                {
                    await context.UserRoles.AddAsync(new UserRole(Guid.NewGuid(), tenantAdminUser.Id, tenantAdminRole.Id));
                    await context.SaveChangesAsync();
                }
            }

            // 5. Seed Department Admin Account
            var deptAdminEmail = "deptadmin@acme.com";
            var deptAdminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == deptAdminEmail);
            if (deptAdminUser == null)
            {
                var passwordHash = passwordHasher.Hash("DeptAdmin@123");
                deptAdminUser = new User(
                    tenantId: acmeTenant.Id,
                    departmentId: itDept.Id,
                    email: deptAdminEmail,
                    passwordHash: passwordHash,
                    firstName: "David",
                    lastName: "Department Lead",
                    phoneNumber: "+84 912 345 678"
                );
                await context.Users.AddAsync(deptAdminUser);
                await context.SaveChangesAsync();

                if (deptAdminRole != null)
                {
                    await context.UserRoles.AddAsync(new UserRole(Guid.NewGuid(), deptAdminUser.Id, deptAdminRole.Id));
                    await context.SaveChangesAsync();
                }
            }

            // 6. Seed Employee / User Account
            var userEmail = "user@acme.com";
            var normalUser = await context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            if (normalUser == null)
            {
                var passwordHash = passwordHasher.Hash("User@123");
                normalUser = new User(
                    tenantId: acmeTenant.Id,
                    departmentId: itDept.Id,
                    email: userEmail,
                    passwordHash: passwordHash,
                    firstName: "Alex",
                    lastName: "Mercer",
                    phoneNumber: "+84 914 829 901"
                );
                await context.Users.AddAsync(normalUser);
                await context.SaveChangesAsync();

                if (employeeRole != null)
                {
                    await context.UserRoles.AddAsync(new UserRole(Guid.NewGuid(), normalUser.Id, employeeRole.Id));
                    await context.SaveChangesAsync();
                }
            }

            logger.LogInformation("Demo Tenants, Departments, and 4 Role accounts seeded successfully.");
        }
    }
}
