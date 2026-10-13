using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
namespace CloudDms.IntegrationTests.Persistence;
public sealed class SearchReadSchemaTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql("Server=localhost;Database=model_only;User ID=model;Password=unused", new MySqlServerVersion(new Version(8,0,0))).Options, new DateTimeProvider());
    [Theory]
    [InlineData("FileContentVersion", "FileId,DocumentVersion")]
    [InlineData("FileTextChunk", "GenerationId,Ordinal")]
    [InlineData("FilePreviewArtifact", "FileId,DocumentVersion,ConverterVersion,Format")]
    [InlineData("TeamMember", "TeamId,UserId")]
    public void ModelEnforcesDocumentCacheAndMembershipUniqueness(string name, string columns)
    {
        using var context = CreateContext();
        var entity = context.Model.GetEntityTypes().SingleOrDefault(x => x.ClrType.Name == name);
        Assert.NotNull(entity);
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && string.Join(",", x.Properties.Select(p => p.Name)) == columns);
        Assert.Contains(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Tenant));
        Assert.All(entity.GetForeignKeys(), x => Assert.NotEqual(DeleteBehavior.Cascade, x.DeleteBehavior));
    }
    [Fact]
    public void DocumentEventsHaveSeparateTableAndRetryLeaseIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.GetEntityTypes().SingleOrDefault(x => x.ClrType.Name == "DocumentOutboxMessage");
        Assert.NotNull(entity);
        Assert.NotEqual(context.Model.FindEntityType(typeof(OutboxMessage))!.GetTableName(), entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), x => string.Join(",", x.Properties.Select(p => p.Name)) == "Status,NextAttemptAt,LeaseExpiresAt");
    }
}

[Collection(MySqlSchemaCollection.Name)]
public sealed class SearchReadDatabaseSchemaTests(CloudDms.IntegrationTests.Support.MySqlFixture fixture)
{
    private async Task<(AppDbContext Context, FileContentVersion Version)> CreateVersion()
    {
        var context = fixture.CreateDbContext();
        var provider = new StorageProvider { Name = "schema-test", ConfigEncrypted = "test-only" };
        var version = new FileContentVersion
        {
            TenantId = CloudDms.IntegrationTests.Support.MySqlFixture.LegacyTenantId,
            FileId = CloudDms.IntegrationTests.Support.MySqlFixture.LegacyFileId,
            DocumentVersion = DateTime.UtcNow.Ticks,
            ProviderId = provider.Id,
            ObjectKey = $"test/{Guid.NewGuid():N}", Hash = "test-hash", ContentType = "text/plain"
        };
        context.StorageProviders.Add(provider);
        context.FileContentVersions.Add(version);
        await context.SaveChangesAsync();
        return (context, version);
    }
    [Fact]
    public async Task DuplicateVersionIsRejectedAndTenantFileMismatchIsRejected()
    {
        var (context, version) = await CreateVersion();
        await using var disposable = context;
        context.ChangeTracker.Clear();
        context.FileContentVersions.Add(new FileContentVersion
        {
            TenantId = version.TenantId, FileId = version.FileId,
            DocumentVersion = version.DocumentVersion, ProviderId = version.ProviderId
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var other = new Tenant(Guid.NewGuid(), "Other", $"T-{Guid.NewGuid():N}", 1000);
        context.Tenants.Add(other);
        await context.SaveChangesAsync();
        context.FileContentVersions.Add(new FileContentVersion
        {
            TenantId = other.Id, FileId = version.FileId,
            DocumentVersion = version.DocumentVersion + 1, ProviderId = version.ProviderId
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
    [Fact]
    public async Task ChunkAndPreviewCachesRejectDuplicates()
    {
        var (context, version) = await CreateVersion();
        await using var disposable = context;
        var generation = new FileSearchGeneration
        {
            TenantId = version.TenantId, FileId = version.FileId, DocumentVersion = version.DocumentVersion,
            ModelVersion = "model-v1", ExtractorVersion = "extract-v1", ChunkerVersion = "chunk-v1"
        };
        context.FileSearchGenerations.Add(generation);
        context.FileTextChunks.Add(new FileTextChunk { TenantId = version.TenantId, GenerationId = generation.Id, Ordinal = 0, Text = "test", TokenCount = 1 });
        context.FilePreviewArtifacts.Add(new FilePreviewArtifact
        {
            TenantId = version.TenantId, FileId = version.FileId, DocumentVersion = version.DocumentVersion,
            ProviderId = version.ProviderId, ConverterVersion = "convert-v1", Format = "pdf", ObjectKey = "test/preview"
        });
        await context.SaveChangesAsync();
        context.FileTextChunks.Add(new FileTextChunk { TenantId = version.TenantId, GenerationId = generation.Id, Ordinal = 0 });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.FilePreviewArtifacts.Add(new FilePreviewArtifact
        {
            TenantId = version.TenantId, FileId = version.FileId, DocumentVersion = version.DocumentVersion,
            ProviderId = version.ProviderId, ConverterVersion = "convert-v1", Format = "pdf"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
    [Fact]
    public async Task MigrationPreservesLegacyFileVersionScanStatusAndRoleUniqueness()
    {
        await using var context = fixture.CreateDbContext();
        var file = await context.FileEntries.SingleAsync(x => x.Id == CloudDms.IntegrationTests.Support.MySqlFixture.LegacyFileId);
        Assert.Equal(1L, file.DocumentVersion);
        Assert.Null(file.ActiveSearchGenerationId);
        Assert.Equal("Infected", file.ScanStatus);
        await using var connection = new MySqlConnector.MySqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Roles' AND INDEX_NAME = 'IX_Roles_Name' AND NON_UNIQUE = 0";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task FreshChainAndUpgradePreserveUnmappedLegacyTable()
    {
        await using var source = fixture.CreateDbContext();
        var connectionString = new MySqlConnector.MySqlConnectionStringBuilder(source.Database.GetConnectionString()!);
        var databaseName = "task1_" + Guid.NewGuid().ToString("N");
        await using var admin = new MySqlConnector.MySqlConnection(connectionString.ConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{databaseName}`";
        await command.ExecuteNonQueryAsync();
        try
        {
            connectionString.Database = databaseName;
            var options = new DbContextOptionsBuilder<AppDbContext>().UseMySql(connectionString.ConnectionString,
                new MySqlServerVersion(new Version(8,4,0))).Options;
            await using var context = new AppDbContext(options, new DateTimeProvider());
            var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync("20261011175833_FinalSync");
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE Products (Id int PRIMARY KEY)");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO Products (Id) VALUES (1)");
            await context.Database.MigrateAsync();
            Assert.Equal(1, await context.Database.ExecuteSqlRawAsync("UPDATE Products SET Id = 2 WHERE Id = 1"));
            var applied = await context.Database.GetAppliedMigrationsAsync();
            Assert.Contains("20261013015430_AddFileSearchRead", applied);
        }
        finally
        {
            command.CommandText = $"DROP DATABASE `{databaseName}`";
            await command.ExecuteNonQueryAsync();
        }
    }
}
