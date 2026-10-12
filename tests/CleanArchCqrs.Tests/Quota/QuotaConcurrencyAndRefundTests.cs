using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.DeleteFile;
using CleanArchCqrs.Application.Features.Files.Commands.PermanentDeleteFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.Quota;

public class QuotaConcurrencyAndRefundTests
{
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<IStorageServiceFactory> _storageFactoryMock = new();
    private readonly Mock<IStorageService> _storageServiceMock = new();

    public QuotaConcurrencyAndRefundTests()
    {
        _storageFactoryMock.Setup(x => x.CreateService(It.IsAny<StorageProvider>()))
            .Returns(_storageServiceMock.Object);
        _storageServiceMock.Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task DeleteFile_ShouldRefundQuotaAcrossAllFourLevels()
    {
        // Arrange: Setup 4-tier Quota hierarchy
        using var context = TestDbContextFactory.Create();

        var provider = new StorageProvider
        {
            Id = Guid.NewGuid(),
            Name = "Local",
            Type = StorageProviderType.Local,
            TotalCapacityBytes = 1000000,
            UsedCapacityBytes = 50000
        };

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Acme Corp",
            Code = "ACME",
            StorageProviderId = provider.Id,
            StorageProvider = provider,
            StorageQuotaBytes = 500000,
            StorageUsedBytes = 50000
        };

        var department = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Engineering",
            Code = "ENG",
            StorageQuotaBytes = 200000,
            StorageUsedBytes = 50000,
            IsActive = true
        };

        var user = new User(tenant.Id, department.Id, "dev@acme.com", "hash", "Dev", "User", null);
        var userQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            QuotaBytes = 100000,
            UsedBytes = 50000
        };

        const long fileSize = 15000;
        var fileEntry = new FileEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DepartmentId = department.Id,
            OwnerId = user.Id,
            Name = "project.zip",
            StorageKey = "project_key",
            ContentType = "application/zip",
            SizeBytes = fileSize,
            StorageProviderId = provider.Id,
            IsDeleted = false
        };

        context.StorageProviders.Add(provider);
        context.Tenants.Add(tenant);
        context.Departments.Add(department);
        context.Users.Add(user);
        context.UserStorageQuotas.Add(userQuota);
        context.FileEntries.Add(fileEntry);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(user.Id);
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenant.Id);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns(department.Id);
        _currentUserServiceMock.Setup(x => x.Role).Returns("Employee");

        var handler = new DeleteFileCommandHandler(
            context,
            _storageFactoryMock.Object,
            _currentUserServiceMock.Object);

        // Act 1: Soft Delete file (chuyển vào thùng rác)
        var softDeleteResult = await handler.Handle(new DeleteFileCommand(fileEntry.Id), CancellationToken.None);

        // Assert 1: Đã xóa mềm, nhưng Quota CHƯA bị giải phóng (giữ nguyên để tránh lạm dụng dung lượng)
        softDeleteResult.IsSuccess.Should().BeTrue();

        var inTrashUserQuota = await context.UserStorageQuotas.FirstAsync(q => q.Id == userQuota.Id);
        var inTrashDept = await context.Departments.FirstAsync(d => d.Id == department.Id);
        var inTrashTenant = await context.Tenants.FirstAsync(t => t.Id == tenant.Id);
        var inTrashProvider = await context.StorageProviders.FirstAsync(p => p.Id == provider.Id);
        var inTrashFile = await context.FileEntries.FirstAsync(f => f.Id == fileEntry.Id);

        inTrashFile.IsDeleted.Should().BeTrue();
        inTrashFile.DeletedAt.Should().NotBeNull();
        inTrashUserQuota.UsedBytes.Should().Be(50000, "xóa mềm không được trừ quota ngay");
        inTrashDept.StorageUsedBytes.Should().Be(50000);
        inTrashTenant.StorageUsedBytes.Should().Be(50000);
        inTrashProvider.UsedCapacityBytes.Should().Be(50000);

        // Act 2: Permanent Delete file (xóa vĩnh viễn)
        var permHandler = new PermanentDeleteFileCommandHandler(
            context,
            _storageFactoryMock.Object,
            _currentUserServiceMock.Object);

        var permResult = await permHandler.Handle(new PermanentDeleteFileCommand(fileEntry.Id), CancellationToken.None);

        // Assert 2: Xóa vĩnh viễn -> Quota được hoàn lại trên cả 4 cấp độ và file bị xóa khỏi DB
        permResult.IsSuccess.Should().BeTrue();

        var reloadedUserQuota = await context.UserStorageQuotas.FirstAsync(q => q.Id == userQuota.Id);
        var reloadedDept = await context.Departments.FirstAsync(d => d.Id == department.Id);
        var reloadedTenant = await context.Tenants.FirstAsync(t => t.Id == tenant.Id);
        var reloadedProvider = await context.StorageProviders.FirstAsync(p => p.Id == provider.Id);
        var deletedFileRecord = await context.FileEntries.FirstOrDefaultAsync(f => f.Id == fileEntry.Id);

        reloadedUserQuota.UsedBytes.Should().Be(50000 - fileSize);
        reloadedDept.StorageUsedBytes.Should().Be(50000 - fileSize);
        reloadedTenant.StorageUsedBytes.Should().Be(50000 - fileSize);
        reloadedProvider.UsedCapacityBytes.Should().Be(50000 - fileSize);
        deletedFileRecord.Should().BeNull();
    }
}
