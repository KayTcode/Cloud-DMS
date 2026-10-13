using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using Xunit;

namespace CloudDms.IntegrationTests.Support;

public sealed class MySqlFixture : IAsyncLifetime
{
    public static readonly Guid LegacyTenantId = Guid.Parse("10000000-0000-0000-0000-000000000101");
    public static readonly Guid LegacyDepartmentId = Guid.Parse("20000000-0000-0000-0000-000000000101");
    public static readonly Guid LegacyUserId = Guid.Parse("30000000-0000-0000-0000-000000000101");
    public static readonly Guid LegacyFileId = Guid.Parse("40000000-0000-0000-0000-000000000101");
    public static readonly Guid LegacyQuotaId = Guid.Parse("50000000-0000-0000-0000-000000000101");

    private readonly string _containerName = $"cloud-dms-mysql-tests-{Guid.NewGuid():N}";
    private string? _connectionString;
    private bool _containerStarted;

    public async Task InitializeAsync()
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var envFile = Path.Combine(Path.GetTempPath(), $"cloud-dms-mysql-{Guid.NewGuid():N}.env");

        try
        {
            await File.WriteAllTextAsync(
                envFile,
                $"MYSQL_ROOT_PASSWORD={password}{Environment.NewLine}MYSQL_DATABASE=cloud_dms_test{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await RunDockerAsync(
                "run",
                "--detach",
                "--rm",
                "--name",
                _containerName,
                "--publish",
                "127.0.0.1::3306",
                "--env-file",
                envFile,
                "mysql:8.4");
            _containerStarted = true;
        }
        finally
        {
            if (File.Exists(envFile))
            {
                File.Delete(envFile);
            }
        }

        var publishedPort = await WaitForPublishedPortAsync();
        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = (uint)publishedPort,
            UserID = "root",
            Password = password,
            Database = "cloud_dms_test",
            ConnectionTimeout = 3,
            DefaultCommandTimeout = 15
        }.ConnectionString;

        await WaitForMySqlAsync();
        await using var context = CreateDbContext();
        await context.Database.GetService<IMigrator>()
            .MigrateAsync("20261008055802_AddGoogleIdentity");
        await SeedLegacyAuthorizationRowsAsync(context);
        await context.Database.MigrateAsync();
    }

    public AppDbContext CreateDbContext(bool enableRetryOnFailure = false)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("The MySQL fixture has not been initialized.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                _connectionString,
                new MySqlServerVersion(new Version(8, 4, 0)),
                mySqlOptions =>
                {
                    if (enableRetryOnFailure)
                    {
                        mySqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                    }
                });

        return new AppDbContext(optionsBuilder.Options, new DateTimeProvider());
    }

    public async Task DisposeAsync()
    {
        if (_containerStarted)
        {
            await RunDockerAsync("rm", "--force", _containerName);
        }
    }

    private async Task<int> WaitForPublishedPortAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var published = await RunDockerAsync("port", _containerName, "3306/tcp");
            var portText = published.Trim().Split(':').Last();
            if (int.TryParse(portText, out var port) && port is > 0 and <= 65535)
            {
                return port;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }

    private async Task WaitForMySqlAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(timeout.Token);
                await using var command = new MySqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(timeout.Token);
                return;
            }
            catch (MySqlException) when (!timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
        }
    }

    private static async Task SeedLegacyAuthorizationRowsAsync(AppDbContext context)
    {
        var now = DateTime.UtcNow;

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `Tenants`
                (`Id`, `Name`, `Code`, `Description`, `StorageQuotaBytes`, `StorageUsedBytes`, `IsActive`, `CreatedAt`, `UpdatedAt`)
            VALUES
                ({LegacyTenantId}, 'Legacy test tenant', 'LEGACY-TEST', NULL, 1000, 10, 1, {now}, NULL);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `Departments`
                (`Id`, `TenantId`, `Name`, `Code`, `Description`, `IsActive`, `CreatedAt`, `UpdatedAt`)
            VALUES
                ({LegacyDepartmentId}, {LegacyTenantId}, 'Legacy department', 'LEGACY', NULL, 1, {now}, NULL);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `Users`
                (`Id`, `TenantId`, `DepartmentId`, `Email`, `PasswordHash`, `FirstName`, `LastName`,
                 `PhoneNumber`, `IsActive`, `EmailConfirmed`, `GoogleIssuer`, `GoogleSubject`, `LastLoginAt`, `CreatedAt`, `UpdatedAt`)
            VALUES
                ({LegacyUserId}, {LegacyTenantId}, {LegacyDepartmentId}, 'legacy@example.test', 'test-hash',
                 'Legacy', 'User', NULL, 1, 1, NULL, NULL, NULL, {now}, NULL);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `FileEntries`
                (`Id`, `TenantId`, `OwnerId`, `FolderId`, `Name`, `StorageKey`, `ContentType`,
                 `SizeBytes`, `Hash`, `ScanStatus`, `IsDeleted`, `CreatedAt`, `UpdatedAt`)
            VALUES
                ({LegacyFileId}, {LegacyTenantId}, {LegacyUserId}, NULL, 'legacy.txt', 'legacy/storage-key',
                 'text/plain', 10, NULL, 'Infected', 1, {now}, NULL);
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO `UserStorageQuotas`
                (`Id`, `UserId`, `QuotaBytes`, `UsedBytes`, `CreatedAt`, `UpdatedAt`)
            VALUES
                ({LegacyQuotaId}, {LegacyUserId}, 100, 10, {now}, NULL);
            """);
    }

    private static async Task<string> RunDockerAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Docker CLI.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Docker command failed: {error.Trim()}");
        }

        return output.Trim();
    }
}
