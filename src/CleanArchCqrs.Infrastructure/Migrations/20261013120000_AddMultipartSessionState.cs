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
        migrationBuilder.CreateTable("LegacyUploadIntents", table => new
        {
            Id = table.Column<Guid>(type: "char(36)", nullable: false),
            TenantId = table.Column<Guid>(type: "char(36)", nullable: false),
            UserId = table.Column<Guid>(type: "char(36)", nullable: false),
            ProviderId = table.Column<Guid>(type: "char(36)", nullable: false),
            FileId = table.Column<Guid>(type: "char(36)", nullable: false),
            ParentFolderId = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
            Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
            ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
            StorageCleanedAt = table.Column<DateTimeOffset?>(type: "datetime(6)", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
            UpdatedAt = table.Column<DateTime?>(type: "datetime(6)", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_LegacyUploadIntents", x => x.Id);
            table.ForeignKey("FK_LegacyUploadIntents_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_LegacyUploadIntents_StorageProviders_ProviderId", x => x.ProviderId,
                "StorageProviders", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_LegacyUploadIntents_Status_ExpiresAt",
            "LegacyUploadIntents", new[] { "Status", "ExpiresAt" });
        migrationBuilder.CreateIndex("IX_LegacyUploadIntents_TenantId_FileId",
            "LegacyUploadIntents", new[] { "TenantId", "FileId" }, unique: true);
        migrationBuilder.CreateIndex("IX_LegacyUploadIntents_ProviderId",
            "LegacyUploadIntents", "ProviderId");
        migrationBuilder.AddColumn<long>("StagingUsedBytes", "StorageProviders",
            type: "bigint", nullable: false, defaultValue: 0L);
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
        migrationBuilder.AddColumn<DateTimeOffset?>("CompletionLeaseExpiresAt", "FileUploadSessions",
            type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<Guid?>("CompletionOwnerId", "FileUploadSessions",
            type: "char(36)", nullable: true);
        migrationBuilder.AddColumn<long>("PreviousSizeBytes", "FileUploadSessions",
            type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<Guid?>("PreviousProviderId", "FileUploadSessions",
            type: "char(36)", nullable: true);
        migrationBuilder.AddColumn<long>("QuotaReservedBytes", "FileUploadSessions",
            type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>("ProviderReservedBytes", "FileUploadSessions",
            type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.Sql("UPDATE `FileUploadSessions` SET `QuotaUserId` = `UserId` WHERE `QuotaUserId` = '00000000-0000-0000-0000-000000000000'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("LegacyUploadIntents");
        migrationBuilder.DropColumn("StagingUsedBytes", "StorageProviders");
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
        migrationBuilder.DropColumn("CompletionLeaseExpiresAt", "FileUploadSessions");
        migrationBuilder.DropColumn("CompletionOwnerId", "FileUploadSessions");
        migrationBuilder.DropColumn("PreviousSizeBytes", "FileUploadSessions");
        migrationBuilder.DropColumn("PreviousProviderId", "FileUploadSessions");
        migrationBuilder.DropColumn("QuotaReservedBytes", "FileUploadSessions");
        migrationBuilder.DropColumn("ProviderReservedBytes", "FileUploadSessions");
    }
}
