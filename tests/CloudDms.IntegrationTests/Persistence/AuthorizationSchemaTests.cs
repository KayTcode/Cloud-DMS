using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace CloudDms.IntegrationTests.Persistence;

[CollectionDefinition("MySQL authorization schema", DisableParallelization = true)]
public sealed class MySqlSchemaCollection : ICollectionFixture<MySqlFixture>
{
    public const string Name = "MySQL authorization schema";
}

[Collection(MySqlSchemaCollection.Name)]
public sealed class AuthorizationSchemaTests(MySqlFixture fixture)
{
    [Fact]
    public async Task UniqueOperationRejectsDuplicateIdempotencyKeyWithinScope()
    {
        await using var context = fixture.CreateDbContext();
        context.AuthorizationOperations.Add(new AuthorizationOperation
        {
            ScopeKey = "tenant:test",
            IdempotencyKey = "request-1",
            PayloadHash = "hash-a",
            Version = 1
        });
        await context.SaveChangesAsync();

        context.AuthorizationOperations.Add(new AuthorizationOperation
        {
            ScopeKey = "tenant:test",
            IdempotencyKey = "request-1",
            PayloadHash = "hash-b",
            Version = 2
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task FileOwnershipSurvivesOwnerDepartmentChange()
    {
        var tenant = new Tenant(Guid.NewGuid(), "Test tenant", $"T-{Guid.NewGuid():N}", 1_000_000);
        var originalDepartment = new Department(Guid.NewGuid(), tenant.Id, "Original", "ORIGINAL");
        var newDepartment = new Department(Guid.NewGuid(), tenant.Id, "New", "NEW");
        var user = new User(tenant.Id, originalDepartment.Id, "person@example.test", "hash", "Test", "User", null);
        var file = new FileEntry
        {
            TenantId = tenant.Id,
            OwnerId = user.Id,
            OwningDepartmentId = originalDepartment.Id,
            Name = "file.txt",
            StorageKey = $"storage/{Guid.NewGuid():N}",
            ContentType = "text/plain",
            SizeBytes = 10
        };

        await using var context = fixture.CreateDbContext();
        context.Tenants.Add(tenant);
        context.Departments.AddRange(originalDepartment, newDepartment);
        context.Users.Add(user);
        context.FileEntries.Add(file);
        await context.SaveChangesAsync();

        user.ChangeDepartment(newDepartment.Id);
        await context.SaveChangesAsync();

        var savedFile = await context.FileEntries.SingleAsync(item => item.Id == file.Id);
        Assert.Equal(originalDepartment.Id, savedFile.OwningDepartmentId);
    }

    [Fact]
    public async Task DepartmentQuotaCheckConstraintRejectsNegativeValue()
    {
        var tenant = new Tenant(Guid.NewGuid(), "Test tenant", $"T-{Guid.NewGuid():N}", 1_000_000);
        var department = new Department(Guid.NewGuid(), tenant.Id, "Test", "TEST");

        await using var context = fixture.CreateDbContext();
        context.Tenants.Add(tenant);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<MySqlException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "UPDATE `Departments` SET `QuotaBytes` = -1 WHERE `Id` = {0}",
                department.Id));
    }

    [Fact]
    public async Task MigrationBackfillsLegacyOwnershipAndKeepsDeletedFilesClosed()
    {
        await using var context = fixture.CreateDbContext();
        var file = await context.FileEntries.SingleAsync(
            item => item.Id == MySqlFixture.LegacyFileId);
        var quota = await context.UserStorageQuotas.SingleAsync(
            item => item.Id == MySqlFixture.LegacyQuotaId);
        var department = await context.Departments.SingleAsync(
            item => item.Id == MySqlFixture.LegacyDepartmentId);

        Assert.Equal(MySqlFixture.LegacyDepartmentId, file.OwningDepartmentId);
        Assert.Equal(FileLifecycle.Purging, file.Lifecycle);
        Assert.Equal(MySqlFixture.LegacyDepartmentId, quota.DepartmentId);
        Assert.Equal(10, department.UsedBytes);
        Assert.Equal(100, department.QuotaBytes);
    }
}
