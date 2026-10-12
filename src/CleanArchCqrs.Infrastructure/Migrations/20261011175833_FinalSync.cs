using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchCqrs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Folders",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "FileEntries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ScanStatus",
                table: "FileEntries",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Departments",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "StorageQuotaBytes",
                table: "Departments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "StorageUsedBytes",
                table: "Departments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_DepartmentId",
                table: "Folders",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FileEntries_DepartmentId",
                table: "FileEntries",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_FileEntries_Departments_DepartmentId",
                table: "FileEntries",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Folders_Departments_DepartmentId",
                table: "Folders",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FileEntries_Departments_DepartmentId",
                table: "FileEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Folders_Departments_DepartmentId",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_Folders_DepartmentId",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_FileEntries_DepartmentId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "ScanStatus",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "StorageQuotaBytes",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "StorageUsedBytes",
                table: "Departments");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Departments",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });
        }
    }
}
