using Domain.Entities;
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
        ILogger logger)
        {
            try
            {
                await SeedPermissionsAsync(context, logger);
                await SeedRolesAsync(context, logger);
                await SeedRolePermissionsAsync(context, logger);

                logger.LogInformation(
                    "Default RBAC data seeded successfully.");
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

            var roleMap = new Dictionary<string, Role>
            {
                ["SystemAdmin"] = systemAdmin,
                ["TenantAdmin"] = tenantAdmin,
                ["Manager"] = manager,
                ["Employee"] = employee
            };

            foreach (var rolePermission in rolePermissions)
            {
                var role = roleMap[rolePermission.Key];

                foreach (var permissionName in rolePermission.Value)
                {
                    var permission = permissions[permissionName];

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
    }
}
