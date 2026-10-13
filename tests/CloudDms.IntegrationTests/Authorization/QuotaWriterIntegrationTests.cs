using System.Data.Common;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Services;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudDms.IntegrationTests.Authorization;

[Collection("File ACL")]
public sealed class QuotaWriterIntegrationTests(MySqlFixture mysql, RedisFixture redis)
{
    private sealed record Current(Guid? UserId, Guid? SessionId, Guid? TenantId) : ICurrentUserService
    {
        public Guid? DepartmentId => null;
        public string? Email => null;
        public string? Role => "Employee";
        public bool IsAuthenticated => true;
    }

    private sealed class StorageGate : IStorageService, IStorageServiceFactory
    {
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Pause { get; set; }
        public IStorageService CreateService(StorageProvider provider) => this;
        public IStorageService CreateServiceFromRawConfig(StorageProviderType type, string rawConfigJson) => this;
        public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string? parentFolderId = null, CancellationToken cancellationToken = default)
        {
            Arrived.TrySetResult();
            if (Pause) await Release.Task.WaitAsync(cancellationToken);
            return Guid.NewGuid().ToString("N");
        }
        public Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageQuotaDto> GetProviderQuotaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> CreateFolderAsync(string folderName, string? parentFolderId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CleanScanner : IVirusScannerService
    {
        public Task<VirusScanResult> ScanStreamAsync(Stream stream, CancellationToken cancellationToken = default) => Task.FromResult(VirusScanResult.Clean());
    }

    private sealed class PurgeWriteGate : DbCommandInterceptor
    {
        private int _paused;
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task WaitIfTenantUpdateAsync(DbCommand command, CancellationToken ct)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("Tenants", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                Arrived.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            await WaitIfTenantUpdateAsync(command, ct);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            await WaitIfTenantUpdateAsync(command, ct);
            return result;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task UploadAndPurgeKeepAllCountersWhenEitherWriterCommitsFirst(bool purgeCommitsFirst, bool uploadFromOtherTenant)
    {
        await using var setup = mysql.CreateDbContext();
        var provider = new StorageProvider { Name = "shared", ConfigEncrypted = "fixture", TotalCapacityBytes = 1000, UsedCapacityBytes = 100 };
        var tenant = new Tenant(Guid.NewGuid(), "quota", Guid.NewGuid().ToString("N"), 1000) { StorageProviderId = provider.Id, StorageUsedBytes = 100 };
        var otherTenant = new Tenant(Guid.NewGuid(), "other", Guid.NewGuid().ToString("N"), 1000) { StorageProviderId = provider.Id };
        var department = new Department { TenantId = tenant.Id, Name = "quota", Code = "Q", QuotaBytes = 1000, UsedBytes = 100 };
        var otherDepartment = new Department { TenantId = otherTenant.Id, Name = "other quota", Code = "Q", QuotaBytes = 1000 };
        var owner = new User(tenant.Id, department.Id, $"quota-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null);
        var otherOwner = new User(otherTenant.Id, otherDepartment.Id, $"other-quota-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null);
        var session = new RefreshToken(Guid.NewGuid(), owner.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddHours(1), null);
        var quota = new UserStorageQuota { UserId = owner.Id, DepartmentId = department.Id, QuotaBytes = 1000, UsedBytes = 100 };
        var otherQuota = new UserStorageQuota { UserId = otherOwner.Id, DepartmentId = otherDepartment.Id, QuotaBytes = 1000 };
        var file = new FileEntry { TenantId = tenant.Id, OwnerId = owner.Id, OwningDepartmentId = department.Id, StorageProviderId = provider.Id, Name = "old", StorageKey = Guid.NewGuid().ToString("N"), ContentType = "text/plain", SizeBytes = 10, Lifecycle = FileLifecycle.Binned, IsDeleted = true };
        setup.StorageProviders.Add(provider);
        setup.Tenants.AddRange(tenant, otherTenant);
        setup.Departments.AddRange(department, otherDepartment);
        setup.Users.AddRange(owner, otherOwner);
        setup.RefreshTokens.Add(session);
        setup.UserStorageQuotas.AddRange(quota, otherQuota);
        setup.FileEntries.Add(file);
        var roleId = Guid.NewGuid();
        await setup.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Roles (Id, Name, CreatedAt) VALUES ({roleId}, {$"QuotaTest-{roleId:N}"}, {DateTime.UtcNow})");
        var permission = await setup.Permissions.SingleOrDefaultAsync(p => p.Name == "file.delete");
        if (permission is null) { permission = new(Guid.NewGuid(), "file.delete", null); setup.Permissions.Add(permission); }
        setup.RolePermissions.Add(new(Guid.NewGuid(), roleId, permission.Id));
        setup.UserRoles.Add(new UserRole(Guid.NewGuid(), owner.Id, roleId));
        await setup.SaveChangesAsync();

        using var client = redis.CreateClient();
        var readiness = new RedisReadiness(client);
        await new SecurityProjectionBootstrapper(setup, client, readiness).RebuildAsync(default);
        var current = new Current(owner.Id, session.Id, tenant.Id);
        var uploadCurrent = uploadFromOtherTenant ? new Current(otherOwner.Id, null, otherTenant.Id) : current;
        var storage = new StorageGate { Pause = purgeCommitsFirst };
        var uploadContext = CreateContext(uploadCurrent);
        await using var uploadDb = uploadContext;
        var uploader = new UploadFileCommandHandler(uploadDb, storage, new CleanScanner(), uploadCurrent, NullLogger<UploadFileCommandHandler>.Instance);
        using var stream = new MemoryStream(new byte[20]);
        var upload = new UploadFileCommand { FileStream = stream, FileName = "new.txt", ContentType = "text/plain", FileSize = 20 };

        var writeGate = new PurgeWriteGate();
        await using var purgeDb = CreateContext(current, purgeCommitsFirst ? null : writeGate);
        var snapshots = new RedisAccessSnapshotStore(client, readiness);
        var evaluator = new AccessPolicyEvaluator();
        var acl = new FileAclQuery(purgeDb);
        var access = new FileAccessService(snapshots, acl, evaluator);
        var resolver = new FileAccessContextResolver(current, purgeDb, snapshots);
        var coordinator = new AuthorizationMutationCoordinator(purgeDb, client, readiness, NullLogger<AuthorizationMutationCoordinator>.Instance);
        var mutations = new FileMutationService(purgeDb, resolver, snapshots, access, acl, evaluator, coordinator, new MySqlQuotaReleaseWriter(purgeDb));

        if (purgeCommitsFirst)
        {
            var uploading = uploader.Handle(upload, default);
            await storage.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                var receipt = await mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, Guid.NewGuid().ToString("N"), default);
                await new AuthorizationProjectionWriter(purgeDb, client).PublishAsync(receipt.OperationId, default);
            }
            finally { storage.Release.TrySetResult(); }
            Assert.True((await uploading).IsSuccess);
        }
        else
        {
            var purging = mutations.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, Guid.NewGuid().ToString("N"), default);
            await writeGate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try { Assert.True((await uploader.Handle(upload, default)).IsSuccess); }
            finally { writeGate.Release.TrySetResult(); }
            var receipt = await purging;
            await new AuthorizationProjectionWriter(purgeDb, client).PublishAsync(receipt.OperationId, default);
        }

        await using var verify = mysql.CreateDbContext();
        Assert.Equal(uploadFromOtherTenant ? 90 : 110, await verify.Tenants.Where(t => t.Id == tenant.Id).Select(t => t.StorageUsedBytes).SingleAsync());
        Assert.Equal(uploadFromOtherTenant ? 20 : 0, await verify.Tenants.Where(t => t.Id == otherTenant.Id).Select(t => t.StorageUsedBytes).SingleAsync());
        Assert.Equal(uploadFromOtherTenant ? 90 : 110, await verify.Departments.Where(d => d.Id == department.Id).Select(d => d.UsedBytes).SingleAsync());
        Assert.Equal(uploadFromOtherTenant ? 20 : 0, await verify.Departments.Where(d => d.Id == otherDepartment.Id).Select(d => d.UsedBytes).SingleAsync());
        Assert.Equal(uploadFromOtherTenant ? 90 : 110, await verify.UserStorageQuotas.Where(q => q.Id == quota.Id).Select(q => q.UsedBytes).SingleAsync());
        Assert.Equal(uploadFromOtherTenant ? 20 : 0, await verify.UserStorageQuotas.Where(q => q.Id == otherQuota.Id).Select(q => q.UsedBytes).SingleAsync());
        Assert.Equal(110, await verify.StorageProviders.Where(p => p.Id == provider.Id).Select(p => p.UsedCapacityBytes).SingleAsync());
        Assert.NotNull(await verify.FileEntries.Where(f => f.Id == file.Id).Select(f => f.QuotaReleasedAt).SingleAsync());
        Assert.Equal(1, await verify.DocumentOutboxMessages.CountAsync(m => m.FileId == file.Id && m.EventType == "DocumentDeleted"));
    }

    private AppDbContext CreateContext(Current current, DbCommandInterceptor? interceptor = null)
    {
        // Use a distinct context for each writer, as production requests do.
        using var source = mysql.CreateDbContext();
        var connection = source.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 4, 0)));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options, new DateTimeProvider(), current);
    }

    [Fact]
    public async Task LateDepartmentQuotaRejectionRollsBackTenantCounterAndMetadata()
    {
        await using var seed = mysql.CreateDbContext();
        var provider = new StorageProvider { Name = "rollback", ConfigEncrypted = "fixture", TotalCapacityBytes = 1000, UsedCapacityBytes = 100 };
        var tenant = new Tenant(Guid.NewGuid(), "rollback", Guid.NewGuid().ToString("N"), 1000) { StorageProviderId = provider.Id, StorageUsedBytes = 100 };
        var department = new Department { TenantId = tenant.Id, Name = "rollback", Code = "R", QuotaBytes = 1000, UsedBytes = 100 };
        var user = new User(tenant.Id, department.Id, $"rollback-{Guid.NewGuid():N}@example.test", "hash", "A", "B", null);
        var quota = new UserStorageQuota { UserId = user.Id, DepartmentId = department.Id, QuotaBytes = 1000, UsedBytes = 100 };
        seed.StorageProviders.Add(provider);
        seed.Tenants.Add(tenant);
        seed.Departments.Add(department);
        seed.Users.Add(user);
        seed.UserStorageQuotas.Add(quota);
        await seed.SaveChangesAsync();

        var storage = new StorageGate { Pause = true };
        var current = new Current(user.Id, null, tenant.Id);
        await using var uploadDb = CreateContext(current);
        var uploader = new UploadFileCommandHandler(uploadDb, storage, new CleanScanner(), current, NullLogger<UploadFileCommandHandler>.Instance);
        using var stream = new MemoryStream(new byte[20]);
        var uploading = uploader.Handle(new UploadFileCommand { FileStream = stream, FileName = "rejected.txt", ContentType = "text/plain", FileSize = 20 }, default);
        await storage.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using (var concurrent = mysql.CreateDbContext())
            await concurrent.Departments.Where(d => d.Id == department.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.QuotaBytes, 100L));
        storage.Release.TrySetResult();
        Assert.False((await uploading.WaitAsync(TimeSpan.FromSeconds(15))).IsSuccess);

        await using var verify = mysql.CreateDbContext();
        Assert.Equal(100, await verify.Tenants.Where(t => t.Id == tenant.Id).Select(t => t.StorageUsedBytes).SingleAsync());
        Assert.Equal(100, await verify.Departments.Where(d => d.Id == department.Id).Select(d => d.UsedBytes).SingleAsync());
        Assert.Equal(100, await verify.UserStorageQuotas.Where(q => q.Id == quota.Id).Select(q => q.UsedBytes).SingleAsync());
        Assert.Equal(100, await verify.StorageProviders.Where(p => p.Id == provider.Id).Select(p => p.UsedCapacityBytes).SingleAsync());
        Assert.False(await verify.FileEntries.AnyAsync(f => f.TenantId == tenant.Id));
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.TenantId == tenant.Id && a.Action == "Upload"));
    }
}
