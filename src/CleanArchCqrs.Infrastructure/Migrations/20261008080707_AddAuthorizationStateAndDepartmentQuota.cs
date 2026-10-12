using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchCqrs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationStateAndDepartmentQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TEMPORARY TABLE `__AuthorizationMigrationPreflight` (
                    `IsValid` tinyint NOT NULL,
                    CONSTRAINT `CK_AuthorizationMigrationPreflight_IsValid` CHECK (`IsValid` = 1)
                );

                INSERT INTO `__AuthorizationMigrationPreflight` (`IsValid`)
                SELECT IF(
                    EXISTS (
                        SELECT 1
                        FROM `UserStorageQuotas` q
                        INNER JOIN `Users` u ON u.`Id` = q.`UserId`
                        WHERE u.`DepartmentId` IS NULL
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM `UserStorageQuotas` q
                        INNER JOIN `Users` u ON u.`Id` = q.`UserId`
                        INNER JOIN `Departments` d ON d.`Id` = u.`DepartmentId`
                        WHERE u.`TenantId` IS NULL OR d.`TenantId` <> u.`TenantId`
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM `FileEntries` f
                        INNER JOIN `Users` u ON u.`Id` = f.`OwnerId`
                        LEFT JOIN `Departments` d ON d.`Id` = u.`DepartmentId`
                        WHERE u.`DepartmentId` IS NULL OR u.`TenantId` IS NULL
                           OR d.`Id` IS NULL OR d.`TenantId` <> u.`TenantId`
                           OR f.`TenantId` <> u.`TenantId`
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM (
                            SELECT required.`TenantId`, SUM(required.`RequiredBytes`) AS `TotalRequiredBytes`
                            FROM (
                                SELECT d.`TenantId`,
                                    GREATEST(
                                        (
                                            SELECT COALESCE(SUM(f.`SizeBytes`), 0)
                                            FROM `FileEntries` f
                                            INNER JOIN `Users` u ON u.`Id` = f.`OwnerId`
                                            WHERE u.`DepartmentId` = d.`Id`
                                        ),
                                        (
                                            SELECT COALESCE(SUM(q.`QuotaBytes`), 0)
                                            FROM `UserStorageQuotas` q
                                            INNER JOIN `Users` u ON u.`Id` = q.`UserId`
                                            WHERE u.`DepartmentId` = d.`Id`
                                        )
                                    ) AS `RequiredBytes`
                                FROM `Departments` d
                            ) required
                            GROUP BY required.`TenantId`
                        ) tenantRequirements
                        INNER JOIN `Tenants` t ON t.`Id` = tenantRequirements.`TenantId`
                        WHERE tenantRequirements.`TotalRequiredBytes` > t.`StorageQuotaBytes`
                    ),
                    0,
                    1
                );

                DROP TEMPORARY TABLE `__AuthorizationMigrationPreflight`;");

            migrationBuilder.DropForeignKey(
                name: "FK_UserStorageQuotas_Users_UserId",
                table: "UserStorageQuotas");

            migrationBuilder.DropIndex(
                name: "IX_UserStorageQuotas_UserId",
                table: "UserStorageQuotas");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "UserStorageQuotas",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "AuthorizationVersion",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningState",
                table: "Users",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Ready")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "AuthorizationVersion",
                table: "FileEntries",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "Lifecycle",
                table: "FileEntries",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "OwningDepartmentId",
                table: "FileEntries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "StorageProviderId",
                table: "FileEntries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "StorageProviderId",
                table: "Tenants",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QuotaReleasedAt",
                table: "FileEntries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuotaBytes",
                table: "Departments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UsedBytes",
                table: "Departments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(@"
                UPDATE `UserStorageQuotas` q
                INNER JOIN `Users` u ON u.`Id` = q.`UserId`
                SET q.`DepartmentId` = u.`DepartmentId`
                WHERE u.`DepartmentId` IS NOT NULL;

                UPDATE `FileEntries` f
                INNER JOIN `Users` u ON u.`Id` = f.`OwnerId`
                SET f.`OwningDepartmentId` = u.`DepartmentId`
                WHERE u.`DepartmentId` IS NOT NULL;

                UPDATE `FileEntries`
                SET `Lifecycle` = 'Purging'
                WHERE `IsDeleted` = 1;

                UPDATE `Departments` d
                SET d.`UsedBytes` = (
                    SELECT COALESCE(SUM(f.`SizeBytes`), 0)
                    FROM `FileEntries` f
                    WHERE f.`OwningDepartmentId` = d.`Id`
                      AND f.`Lifecycle` IN ('Active', 'Binned', 'Purging')
                );

                UPDATE `Departments` d
                SET d.`QuotaBytes` = GREATEST(
                    d.`UsedBytes`,
                    (
                        SELECT COALESCE(SUM(q.`QuotaBytes`), 0)
                        FROM `UserStorageQuotas` q
                        INNER JOIN `Users` u ON u.`Id` = q.`UserId`
                        WHERE u.`DepartmentId` = d.`Id`
                    )
                );");

            migrationBuilder.AlterColumn<Guid>(
                name: "DepartmentId",
                table: "UserStorageQuotas",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true,
                oldCollation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "StorageProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ConfigEncrypted = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalCapacityBytes = table.Column<long>(type: "bigint", nullable: false),
                    UsedCapacityBytes = table.Column<long>(type: "bigint", nullable: false),
                    RootFolderId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "Connected")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSyncAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_StorageProviders", x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthorizationOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayloadHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CommittedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationOperations", x => x.Id);
                    table.CheckConstraint("CK_AuthorizationOperations_Version_Positive", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthorizationScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    IsUpdating = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ActiveOperationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationScopes", x => x.Id);
                    table.CheckConstraint("CK_AuthorizationScopes_Version_Positive", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AggregateKey = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_Version_Positive", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuotaReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    IdempotencyKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotaReservations", x => x.Id);
                    table.CheckConstraint("CK_QuotaReservations_SizeBytes_Positive", "`SizeBytes` > 0");
                    table.ForeignKey(
                        name: "FK_QuotaReservations_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuotaReservations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuotaReservations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserStorageQuotas_DepartmentId",
                table: "UserStorageQuotas",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStorageQuotas_UserId_DepartmentId",
                table: "UserStorageQuotas",
                columns: new[] { "UserId", "DepartmentId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserStorageQuotas_QuotaBytes_NonNegative",
                table: "UserStorageQuotas",
                sql: "`QuotaBytes` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserStorageQuotas_UsedBytes_Lte_QuotaBytes",
                table: "UserStorageQuotas",
                sql: "`UsedBytes` <= `QuotaBytes`");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserStorageQuotas_UsedBytes_NonNegative",
                table: "UserStorageQuotas",
                sql: "`UsedBytes` >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_FileEntries_OwningDepartmentId",
                table: "FileEntries",
                column: "OwningDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FileEntries_StorageProviderId",
                table: "FileEntries",
                column: "StorageProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_StorageProviderId",
                table: "Tenants",
                column: "StorageProviderId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Departments_QuotaBytes_NonNegative",
                table: "Departments",
                sql: "`QuotaBytes` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Departments_UsedBytes_Lte_QuotaBytes",
                table: "Departments",
                sql: "`UsedBytes` <= `QuotaBytes`");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Departments_UsedBytes_NonNegative",
                table: "Departments",
                sql: "`UsedBytes` >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationOperations_ScopeKey_IdempotencyKey",
                table: "AuthorizationOperations",
                columns: new[] { "ScopeKey", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationOperations_ScopeKey_Version",
                table: "AuthorizationOperations",
                columns: new[] { "ScopeKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationOperations_Status_CreatedAt",
                table: "AuthorizationOperations",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationScopes_IsUpdating",
                table: "AuthorizationScopes",
                column: "IsUpdating");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationScopes_ScopeKey",
                table: "AuthorizationScopes",
                column: "ScopeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AggregateKey_Version",
                table: "OutboxMessages",
                columns: new[] { "AggregateKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "PublishedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QuotaReservations_DepartmentId_State",
                table: "QuotaReservations",
                columns: new[] { "DepartmentId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_QuotaReservations_IdempotencyKey",
                table: "QuotaReservations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuotaReservations_TenantId_State",
                table: "QuotaReservations",
                columns: new[] { "TenantId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_QuotaReservations_UserId_State",
                table: "QuotaReservations",
                columns: new[] { "UserId", "State" });

            migrationBuilder.AddForeignKey(
                name: "FK_FileEntries_Departments_OwningDepartmentId",
                table: "FileEntries",
                column: "OwningDepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserStorageQuotas_Departments_DepartmentId",
                table: "UserStorageQuotas",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserStorageQuotas_Users_UserId",
                table: "UserStorageQuotas",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FileEntries_StorageProviders_StorageProviderId",
                table: "FileEntries",
                column: "StorageProviderId",
                principalTable: "StorageProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_StorageProviders_StorageProviderId",
                table: "Tenants",
                column: "StorageProviderId",
                principalTable: "StorageProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FileEntries_Departments_OwningDepartmentId",
                table: "FileEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_FileEntries_StorageProviders_StorageProviderId",
                table: "FileEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_StorageProviders_StorageProviderId",
                table: "Tenants");

            migrationBuilder.DropForeignKey(
                name: "FK_UserStorageQuotas_Departments_DepartmentId",
                table: "UserStorageQuotas");

            migrationBuilder.DropForeignKey(
                name: "FK_UserStorageQuotas_Users_UserId",
                table: "UserStorageQuotas");

            migrationBuilder.DropTable(
                name: "AuthorizationOperations");

            migrationBuilder.DropTable(
                name: "AuthorizationScopes");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "QuotaReservations");

            migrationBuilder.DropIndex(
                name: "IX_UserStorageQuotas_DepartmentId",
                table: "UserStorageQuotas");

            migrationBuilder.DropIndex(
                name: "IX_UserStorageQuotas_UserId_DepartmentId",
                table: "UserStorageQuotas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserStorageQuotas_QuotaBytes_NonNegative",
                table: "UserStorageQuotas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserStorageQuotas_UsedBytes_Lte_QuotaBytes",
                table: "UserStorageQuotas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserStorageQuotas_UsedBytes_NonNegative",
                table: "UserStorageQuotas");

            migrationBuilder.DropIndex(
                name: "IX_FileEntries_OwningDepartmentId",
                table: "FileEntries");

            migrationBuilder.DropIndex(
                name: "IX_FileEntries_StorageProviderId",
                table: "FileEntries");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_StorageProviderId",
                table: "Tenants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Departments_QuotaBytes_NonNegative",
                table: "Departments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Departments_UsedBytes_Lte_QuotaBytes",
                table: "Departments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Departments_UsedBytes_NonNegative",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "UserStorageQuotas");

            migrationBuilder.DropColumn(
                name: "AuthorizationVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProvisioningState",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthorizationVersion",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "Lifecycle",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "OwningDepartmentId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "QuotaReleasedAt",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "StorageProviderId",
                table: "FileEntries");

            migrationBuilder.DropColumn(
                name: "StorageProviderId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "QuotaBytes",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "UsedBytes",
                table: "Departments");

            migrationBuilder.DropTable(
                name: "StorageProviders");

            migrationBuilder.CreateIndex(
                name: "IX_UserStorageQuotas_UserId",
                table: "UserStorageQuotas",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserStorageQuotas_Users_UserId",
                table: "UserStorageQuotas",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
