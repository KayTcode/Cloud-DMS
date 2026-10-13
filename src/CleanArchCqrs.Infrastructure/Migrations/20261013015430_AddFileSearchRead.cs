using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchCqrs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileSearchRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<long>(
                name: "AuthorizationVersion",
                table: "Folders",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveSearchGenerationId",
                table: "FileEntries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "FileEntries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "FileEntries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "DocumentVersion",
                table: "FileEntries",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FileEntries_TenantId_Id",
                table: "FileEntries",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "DocumentOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Payload = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    LeaseOwnerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentOutboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentOutboxMessages_FileEntries_TenantId_FileId",
                        columns: x => new { x.TenantId, x.FileId },
                        principalTable: "FileEntries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentOutboxMessages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FileContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ObjectKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ContentType = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ScanStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileContentVersions", x => x.Id);
                    table.UniqueConstraint("AK_FileContentVersions_TenantId_FileId_DocumentVersion", x => new { x.TenantId, x.FileId, x.DocumentVersion });
                    table.ForeignKey(
                        name: "FK_FileContentVersions_FileEntries_TenantId_FileId",
                        columns: x => new { x.TenantId, x.FileId },
                        principalTable: "FileEntries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileContentVersions_StorageProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "StorageProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileContentVersions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FileUploadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProviderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ObjectKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReservedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileUploadSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileUploadSessions_StorageProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "StorageProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileUploadSessions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileUploadSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ResourceGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResourceKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResourceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PrincipalKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrincipalId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceGrants_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorizationVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.UniqueConstraint("AK_Teams_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Teams_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FilePreviewArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConverterVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Format = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ObjectKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilePreviewArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilePreviewArtifacts_FileContentVersions_TenantId_FileId_Doc~",
                        columns: x => new { x.TenantId, x.FileId, x.DocumentVersion },
                        principalTable: "FileContentVersions",
                        principalColumns: new[] { "TenantId", "FileId", "DocumentVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FilePreviewArtifacts_StorageProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "StorageProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FilePreviewArtifacts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FileSearchGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    ModelVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExtractorVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChunkerVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPartial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FailureCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSearchGenerations", x => x.Id);
                    table.UniqueConstraint("AK_FileSearchGenerations_TenantId_FileId_Id", x => new { x.TenantId, x.FileId, x.Id });
                    table.UniqueConstraint("AK_FileSearchGenerations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_FileSearchGenerations_FileContentVersions_TenantId_FileId_Do~",
                        columns: x => new { x.TenantId, x.FileId, x.DocumentVersion },
                        principalTable: "FileContentVersions",
                        principalColumns: new[] { "TenantId", "FileId", "DocumentVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileSearchGenerations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TeamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TenantId_TeamId",
                        columns: x => new { x.TenantId, x.TeamId },
                        principalTable: "Teams",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FileTextChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    GenerationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TokenCount = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTextChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileTextChunks_FileSearchGenerations_TenantId_GenerationId",
                        columns: x => new { x.TenantId, x.GenerationId },
                        principalTable: "FileSearchGenerations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileTextChunks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FileEntries_DeletedByUserId",
                table: "FileEntries",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileEntries_TenantId_Id_ActiveSearchGenerationId",
                table: "FileEntries",
                columns: new[] { "TenantId", "Id", "ActiveSearchGenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentOutboxMessages_Status_NextAttemptAt_LeaseExpiresAt",
                table: "DocumentOutboxMessages",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentOutboxMessages_TenantId",
                table: "DocumentOutboxMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentOutboxMessages_TenantId_FileId_DocumentVersion",
                table: "DocumentOutboxMessages",
                columns: new[] { "TenantId", "FileId", "DocumentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentOutboxMessages_TenantId_IdempotencyKey",
                table: "DocumentOutboxMessages",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileContentVersions_FileId_DocumentVersion",
                table: "FileContentVersions",
                columns: new[] { "FileId", "DocumentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileContentVersions_ProviderId",
                table: "FileContentVersions",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_FileContentVersions_TenantId",
                table: "FileContentVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FilePreviewArtifacts_FileId_DocumentVersion_ConverterVersion~",
                table: "FilePreviewArtifacts",
                columns: new[] { "FileId", "DocumentVersion", "ConverterVersion", "Format" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilePreviewArtifacts_ProviderId",
                table: "FilePreviewArtifacts",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_FilePreviewArtifacts_TenantId",
                table: "FilePreviewArtifacts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FilePreviewArtifacts_TenantId_FileId_DocumentVersion",
                table: "FilePreviewArtifacts",
                columns: new[] { "TenantId", "FileId", "DocumentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_FileSearchGenerations_TenantId",
                table: "FileSearchGenerations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FileSearchGenerations_TenantId_FileId_DocumentVersion_Status",
                table: "FileSearchGenerations",
                columns: new[] { "TenantId", "FileId", "DocumentVersion", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileTextChunks_GenerationId_Ordinal",
                table: "FileTextChunks",
                columns: new[] { "GenerationId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileTextChunks_TenantId",
                table: "FileTextChunks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FileTextChunks_TenantId_GenerationId",
                table: "FileTextChunks",
                columns: new[] { "TenantId", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_ProviderId",
                table: "FileUploadSessions",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_Status_ExpiresAt",
                table: "FileUploadSessions",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_TenantId",
                table: "FileUploadSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_TenantId_UserId_IdempotencyKey",
                table: "FileUploadSessions",
                columns: new[] { "TenantId", "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadSessions_UserId",
                table: "FileUploadSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGrants_TenantId",
                table: "ResourceGrants",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGrants_TenantId_PrincipalKind_PrincipalId_ExpiresAt",
                table: "ResourceGrants",
                columns: new[] { "TenantId", "PrincipalKind", "PrincipalId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGrants_TenantId_ResourceKind_ResourceId_PrincipalKin~",
                table: "ResourceGrants",
                columns: new[] { "TenantId", "ResourceKind", "ResourceId", "PrincipalKind", "PrincipalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_UserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId",
                table: "TeamMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId_TeamId",
                table: "TeamMembers",
                columns: new[] { "TenantId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TenantId_UserId",
                table: "TeamMembers",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_UserId",
                table: "TeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TenantId",
                table: "Teams",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_FileEntries_FileSearchGenerations_TenantId_Id_ActiveSearchGe~",
                table: "FileEntries",
                columns: new[] { "TenantId", "Id", "ActiveSearchGenerationId" },
                principalTable: "FileSearchGenerations",
                principalColumns: new[] { "TenantId", "FileId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FileEntries_Users_DeletedByUserId",
                table: "FileEntries",
                column: "DeletedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropForeignKey(
                name: "FK_FileEntries_FileSearchGenerations_TenantId_Id_ActiveSearchGe~",
                table: "FileEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_FileEntries_Users_DeletedByUserId",
                table: "FileEntries");

            migrationBuilder.DropTable(
                name: "DocumentOutboxMessages");

            migrationBuilder.DropTable(
                name: "FilePreviewArtifacts");

            migrationBuilder.DropTable(
                name: "FileTextChunks");

            migrationBuilder.DropTable(
                name: "FileUploadSessions");

            migrationBuilder.DropTable(
                name: "ResourceGrants");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "FileSearchGenerations");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "FileContentVersions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FileEntries_TenantId_Id",
                table: "FileEntries");

            migrationBuilder.DropIndex(
                name: "IX_FileEntries_DeletedByUserId",
                table: "FileEntries");

            migrationBuilder.DropIndex(
                name: "IX_FileEntries_TenantId_Id_ActiveSearchGenerationId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "AuthorizationVersion",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "ActiveSearchGenerationId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "DocumentVersion",
                table: "FileEntries");

        }
    }
}
