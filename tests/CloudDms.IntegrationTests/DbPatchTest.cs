using MySqlConnector;
using Xunit;

namespace CloudDms.IntegrationTests;

public class DbPatchTest
{
    [Fact]
    public async Task ApplyMissingColumns()
    {
        var connStr = "Server=localhost;Port=3306;Database=CloudDmsDb;User=root;Password=Thanhedo2002@@;CharSet=utf8mb4;";
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();

        var sqls = new[]
        {
            "ALTER TABLE `AuthorizationOperations` ADD COLUMN `InitiatedByUserId` char(36) COLLATE ascii_general_ci NULL;",
            "ALTER TABLE `Folders` ADD COLUMN `DepartmentId` char(36) COLLATE ascii_general_ci NULL;",
            "ALTER TABLE `FileEntries` ADD COLUMN `DepartmentId` char(36) COLLATE ascii_general_ci NULL;",
            "ALTER TABLE `Departments` ADD COLUMN `StorageQuotaBytes` bigint NOT NULL DEFAULT 0;",
            "ALTER TABLE `Departments` ADD COLUMN `StorageUsedBytes` bigint NOT NULL DEFAULT 0;"
        };

        foreach (var sql in sqls)
        {
            try
            {
                await using var cmd = new MySqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1060)
            {
                // Duplicate column name, ignore
            }
        }
    }

    [Fact]
    public async Task InitializeRedisReadiness()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CleanArchCqrs.Infrastructure.Security.RedisOptions
        {
            ConnectionString = "localhost:6379,abortConnect=false",
            CommandTimeoutMilliseconds = 1000,
            ConnectTimeoutMilliseconds = 2000,
            CircuitFailureThreshold = 3,
            CircuitOpenMilliseconds = 5000
        });
        using var client = new CleanArchCqrs.Infrastructure.Security.RedisSecurityClient(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CleanArchCqrs.Infrastructure.Security.RedisSecurityClient>.Instance);
        var readiness = new CleanArchCqrs.Infrastructure.Security.RedisReadiness(client);
        var runId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
        var generation = Guid.NewGuid().ToString("N");
        await readiness.SetReadyAsync(generation, runId, CancellationToken.None);
        var readyGen = await readiness.AssertReadyAsync(CancellationToken.None);
        Assert.Equal(generation, readyGen);
    }
}
