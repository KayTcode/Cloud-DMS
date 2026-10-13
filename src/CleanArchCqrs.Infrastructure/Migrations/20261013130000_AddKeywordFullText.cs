using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CleanArchCqrs.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261013130000_AddKeywordFullText")]
public sealed class AddKeywordFullText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE FULLTEXT INDEX `IX_FileEntries_Name` ON `FileEntries` (`Name`)");
        migrationBuilder.Sql("CREATE FULLTEXT INDEX `IX_FileTextChunks_Text` ON `FileTextChunks` (`Text`)");
        migrationBuilder.CreateIndex("IX_FileEntries_TenantId_Lifecycle_IsDeleted_FolderId", "FileEntries",
            new[] { "TenantId", "Lifecycle", "IsDeleted", "FolderId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_FileEntries_TenantId_Lifecycle_IsDeleted_FolderId", "FileEntries");
        migrationBuilder.DropIndex("IX_FileTextChunks_Text", "FileTextChunks");
        migrationBuilder.DropIndex("IX_FileEntries_Name", "FileEntries");
    }
}
