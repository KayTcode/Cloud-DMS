using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CleanArchCqrs.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261013120000_AddMultipartSessionState")]
public sealed class AddMultipartSessionState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("MultipartUploadId", "FileUploadSessions",
            type: "varchar(500)", maxLength: 500, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("FileName", "FileUploadSessions",
            type: "varchar(255)", maxLength: 255, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("ContentType", "FileUploadSessions",
            type: "varchar(255)", maxLength: 255, nullable: false,
            defaultValue: "application/octet-stream");
        migrationBuilder.AddColumn<Guid?>("FolderId", "FileUploadSessions", type: "char(36)", nullable: true);
        migrationBuilder.AddColumn<Guid?>("DepartmentId", "FileUploadSessions", type: "char(36)", nullable: true);
        migrationBuilder.AddColumn<Guid>("QuotaUserId", "FileUploadSessions", type: "char(36)", nullable: false,
            defaultValue: Guid.Empty);
        migrationBuilder.AddColumn<long?>("PreviousDocumentVersion", "FileUploadSessions",
            type: "bigint", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset?>("StorageCleanedAt", "FileUploadSessions",
            type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<string>("RequestHash", "FileUploadSessions", type: "varchar(64)",
            maxLength: 64, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("CompletionManifestHash", "FileUploadSessions",
            type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.Sql("UPDATE `FileUploadSessions` SET `QuotaUserId` = `UserId` WHERE `QuotaUserId` = '00000000-0000-0000-0000-000000000000'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("MultipartUploadId", "FileUploadSessions");
        migrationBuilder.DropColumn("FileName", "FileUploadSessions");
        migrationBuilder.DropColumn("ContentType", "FileUploadSessions");
        migrationBuilder.DropColumn("FolderId", "FileUploadSessions");
        migrationBuilder.DropColumn("DepartmentId", "FileUploadSessions");
        migrationBuilder.DropColumn("QuotaUserId", "FileUploadSessions");
        migrationBuilder.DropColumn("PreviousDocumentVersion", "FileUploadSessions");
        migrationBuilder.DropColumn("StorageCleanedAt", "FileUploadSessions");
        migrationBuilder.DropColumn("RequestHash", "FileUploadSessions");
        migrationBuilder.DropColumn("CompletionManifestHash", "FileUploadSessions");
    }
}
