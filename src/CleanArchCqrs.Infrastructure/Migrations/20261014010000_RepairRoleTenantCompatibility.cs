using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CleanArchCqrs.Infrastructure.Migrations;

// Compatibility repair only: never rebuild Roles or change existing IDs/name uniqueness.
[DbContext(typeof(AppDbContext))]
[Migration("20261014010000_RepairRoleTenantCompatibility")]
public sealed class RepairRoleTenantCompatibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "TenantId", table: "Roles", type: "char(36)", nullable: true, collation: "ascii_general_ci");
        migrationBuilder.CreateIndex(name: "IX_Roles_TenantId_Name", table: "Roles", columns: new[] { "TenantId", "Name" }, unique: true);
        migrationBuilder.AddForeignKey(name: "FK_Roles_Tenants_TenantId", table: "Roles", column: "TenantId", principalTable: "Tenants", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        // Align shadow defaults without dropping or replacing the physical legacy columns.
        migrationBuilder.Sql("ALTER TABLE `Departments` ALTER COLUMN `StorageQuotaBytes` SET DEFAULT 0, ALTER COLUMN `StorageUsedBytes` SET DEFAULT 0;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately additive: rollback keeps compatibility data instead of dropping it.
    }
}
