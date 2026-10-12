using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.Files;

public class FileUploadSecurityTests
{
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<IStorageServiceFactory> _storageFactoryMock = new();
    private readonly Mock<IVirusScannerService> _virusScannerMock = new();
    private readonly Mock<ILogger<UploadFileCommandHandler>> _loggerMock = new();

    public FileUploadSecurityTests()
    {
        _virusScannerMock.Setup(x => x.ScanStreamAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VirusScanResult.Clean());
    }

    [Fact]
    public async Task Handle_WhenCallerIsSystemAdmin_ShouldRejectImmediately()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var sysAdminUser = new User(null, null, "admin@clouddms.com", "hash", "System", "Admin", null);
        var sysAdminRole = new Role { Id = Guid.NewGuid(), Name = "SystemAdmin" };
        context.Users.Add(sysAdminUser);
        context.Roles.Add(sysAdminRole);
        context.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = sysAdminUser.Id, RoleId = sysAdminRole.Id });
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(sysAdminUser.Id);
        _currentUserServiceMock.Setup(x => x.Role).Returns("SystemAdmin");

        var handler = new UploadFileCommandHandler(
            context,
            _storageFactoryMock.Object,
            _virusScannerMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var command = new UploadFileCommand
        {
            FileName = "test.pdf",
            ContentType = "application/pdf",
            FileSize = 3,
            FileStream = stream
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("SystemAdmin");
    }

    [Fact]
    public async Task Handle_WhenUserHasNoQuota_ShouldRejectImmediately()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Acme Corp", Code = "ACME" };
        var provider = new StorageProvider { Id = Guid.NewGuid(), Name = "Local", Type = StorageProviderType.Local, TotalCapacityBytes = 1000000 };
        tenant.StorageProviderId = provider.Id;
        tenant.StorageProvider = provider;

        var department = new Department { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "IT", Code = "IT", StorageQuotaBytes = 50000, IsActive = true };
        var employee = new User(tenant.Id, department.Id, "emp@acme.com", "hash", "John", "Doe", null);
        var employeeRole = new Role { Id = Guid.NewGuid(), Name = "Employee" };

        context.StorageProviders.Add(provider);
        context.Tenants.Add(tenant);
        context.Departments.Add(department);
        context.Users.Add(employee);
        context.Roles.Add(employeeRole);
        context.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = employee.Id, RoleId = employeeRole.Id });
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(employee.Id);
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenant.Id);
        _currentUserServiceMock.Setup(x => x.Role).Returns("Employee");

        var handler = new UploadFileCommandHandler(
            context,
            _storageFactoryMock.Object,
            _virusScannerMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var command = new UploadFileCommand
        {
            FileName = "document.pdf",
            ContentType = "application/pdf",
            FileSize = 3,
            FileStream = stream
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("chưa được cấp hạn mức lưu trữ cá nhân");
    }

    [Fact]
    public async Task Handle_WhenFileSizeExceedsUserQuota_ShouldRejectWithQuotaExceededMessage()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Acme Corp", Code = "ACME" };
        var provider = new StorageProvider { Id = Guid.NewGuid(), Name = "Local", Type = StorageProviderType.Local, TotalCapacityBytes = 1000000 };
        tenant.StorageProviderId = provider.Id;
        tenant.StorageProvider = provider;

        var department = new Department { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "IT", Code = "IT", StorageQuotaBytes = 50000, IsActive = true };
        var employee = new User(tenant.Id, department.Id, "emp@acme.com", "hash", "John", "Doe", null);
        var employeeRole = new Role { Id = Guid.NewGuid(), Name = "Employee" };

        // Quota is only 100 bytes, already used 90 bytes -> available 10 bytes
        var userQuota = new UserStorageQuota { Id = Guid.NewGuid(), UserId = employee.Id, QuotaBytes = 100, UsedBytes = 90 };

        context.StorageProviders.Add(provider);
        context.Tenants.Add(tenant);
        context.Departments.Add(department);
        context.Users.Add(employee);
        context.Roles.Add(employeeRole);
        context.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = employee.Id, RoleId = employeeRole.Id });
        context.UserStorageQuotas.Add(userQuota);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(employee.Id);
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenant.Id);
        _currentUserServiceMock.Setup(x => x.Role).Returns("Employee");

        var handler = new UploadFileCommandHandler(
            context,
            _storageFactoryMock.Object,
            _virusScannerMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        // Uploading 20 bytes -> exceeds available 10 bytes
        using var stream = new MemoryStream(new byte[20]);
        var command = new UploadFileCommand
        {
            FileName = "large.pdf",
            ContentType = "application/pdf",
            FileSize = 20,
            FileStream = stream
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("hết hạn mức lưu trữ cá nhân");
    }
}
