using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchCqrs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationOperationOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InitiatedByUserId",
                table: "AuthorizationOperations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationOperations_InitiatedByUserId_CreatedAt",
                table: "AuthorizationOperations",
                columns: new[] { "InitiatedByUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthorizationOperations_InitiatedByUserId_CreatedAt",
                table: "AuthorizationOperations");

            migrationBuilder.DropColumn(
                name: "InitiatedByUserId",
                table: "AuthorizationOperations");
        }
    }
}
