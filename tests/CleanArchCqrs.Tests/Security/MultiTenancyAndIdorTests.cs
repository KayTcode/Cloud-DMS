using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Queries.DownloadFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.Security;

public class MultiTenancyAndIdorTests
{
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<IStorageServiceFactory> _storageFactoryMock = new();
    private readonly Mock<IStorageService> _storageServiceMock = new();

    public MultiTenancyAndIdorTests()
    {
        _storageFactoryMock.Setup(x => x.CreateService(It.IsAny<StorageProvider>()))
            .Returns(_storageServiceMock.Object);
        _storageServiceMock.Setup(x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task DownloadFile_WhenUserBelongsToDifferentTenant_ShouldPreventAccess()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var provider = new StorageProvider { Id = Guid.NewGuid(), Name = "Local", Type = StorageProviderType.Local };
        context.StorageProviders.Add(provider);

        var fileTenantA = new FileEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantAId,
            OwnerId = Guid.NewGuid(),
            Name = "secret_a.pdf",
            StorageKey = "key_a",
            ContentType = "application/pdf",
            StorageProviderId = provider.Id,
            IsDeleted = false
        };
        context.FileEntries.Add(fileTenantA);
        await context.SaveChangesAsync();

        // Caller belongs to Tenant B
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantBId);
        _currentUserServiceMock.Setup(x => x.Role).Returns("TenantAdmin");

        var handler = new DownloadFileQueryHandler(
            context,
            _storageFactoryMock.Object,
            _currentUserServiceMock.Object);

        // Act
        var result = await handler.Handle(new DownloadFileQuery(fileTenantA.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Tệp tin không tồn tại hoặc đã bị xóa");
    }

    [Fact]
    public async Task DownloadFile_WhenEmployeeBelongsToDifferentDepartment_ShouldPreventAccess()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var deptSales = Guid.NewGuid();
        var deptHr = Guid.NewGuid();

        var provider = new StorageProvider { Id = Guid.NewGuid(), Name = "Local", Type = StorageProviderType.Local };
        context.StorageProviders.Add(provider);

        var fileSales = new FileEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DepartmentId = deptSales,
            OwnerId = Guid.NewGuid(),
            Name = "sales_report.pdf",
            StorageKey = "key_sales",
            ContentType = "application/pdf",
            StorageProviderId = provider.Id,
            IsDeleted = false
        };
        context.FileEntries.Add(fileSales);
        await context.SaveChangesAsync();

        // Caller is Employee from HR
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantId);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns(deptHr);
        _currentUserServiceMock.Setup(x => x.Role).Returns("Employee");

        var handler = new DownloadFileQueryHandler(
            context,
            _storageFactoryMock.Object,
            _currentUserServiceMock.Object);

        // Act
        var result = await handler.Handle(new DownloadFileQuery(fileSales.Id), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Tệp tin không tồn tại hoặc đã bị xóa");
    }
}
