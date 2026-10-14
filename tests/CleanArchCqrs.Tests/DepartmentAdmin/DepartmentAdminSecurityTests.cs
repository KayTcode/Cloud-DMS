using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.AllocateUserStorageQuota;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.CreateEmployee;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.SetupDepartment;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentUsersStorageQuota;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.Tests.DepartmentAdmin;

public class DepartmentAdminSecurityTests
{
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<ILogger<AllocateUserStorageQuotaCommandHandler>> _loggerMock = new();

    private static (Tenant tenantA, Tenant tenantB, Department deptA, Department deptB, User userInDeptA) SeedTestData(
        Infrastructure.Persistence.AppDbContext context)
    {
        var tenantA = new Tenant(Guid.NewGuid(), "Tenant Alpha", "ALPHA", 100L * 1024 * 1024 * 1024);
        var tenantB = new Tenant(Guid.NewGuid(), "Tenant Beta", "BETA", 100L * 1024 * 1024 * 1024);
        context.Tenants.AddRange(tenantA, tenantB);

        var deptA = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Phòng IT Alpha",
            Code = "IT-A",
            StorageQuotaBytes = 50L * 1024 * 1024 * 1024,
            StorageUsedBytes = 0,
            IsActive = true
        };

        var deptB = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            Name = "Phòng IT Beta",
            Code = "IT-B",
            StorageQuotaBytes = 50L * 1024 * 1024 * 1024,
            StorageUsedBytes = 0,
            IsActive = true
        };
        context.Departments.AddRange(deptA, deptB);

        var userInDeptA = new User(tenantA.Id, deptA.Id, "emp.a@alpha.com", "hash", "Van", "Nguyen", null);
        context.Users.Add(userInDeptA);

        context.SaveChanges();
        return (tenantA, tenantB, deptA, deptB, userInDeptA);
    }

    [Fact]
    public async Task AllocateUserQuota_WhenTenantAdminTriesToModifyOtherTenantDepartment_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenantA, tenantB, _, deptB, _) = SeedTestData(context);

        // Caller là TenantAdmin của Tenant Alpha
        _currentUserServiceMock.Setup(x => x.Role).Returns("TenantAdmin");
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantA.Id);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns((Guid?)null);

        var handler = new AllocateUserStorageQuotaCommandHandler(
            context,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        // Cố tình chỉnh phòng ban thuộc Tenant Beta
        var command = new AllocateUserStorageQuotaCommand(deptB.Id, Guid.NewGuid(), 1024);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Phòng ban không thuộc tổ chức của bạn.");
    }

    [Fact]
    public async Task AllocateUserQuota_WhenDepartmentAdminTriesToModifyOtherDepartment_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenantA, _, deptA, _, _) = SeedTestData(context);

        var deptA2 = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Phòng Nhân sự Alpha",
            Code = "HR-A",
            StorageQuotaBytes = 20L * 1024 * 1024 * 1024,
            IsActive = true
        };
        context.Departments.Add(deptA2);
        await context.SaveChangesAsync();

        // Caller là DepartmentAdmin của Dept A
        _currentUserServiceMock.Setup(x => x.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantA.Id);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns(deptA.Id);

        var handler = new AllocateUserStorageQuotaCommandHandler(
            context,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        // Cố tình chỉnh phòng ban Dept A2
        var command = new AllocateUserStorageQuotaCommand(deptA2.Id, Guid.NewGuid(), 1024);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Bạn không có quyền quản lý hạn mức của phòng ban này.");
    }

    [Fact]
    public async Task AllocateUserQuota_WhenCallerHasNoDepartmentId_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenantA, _, deptA, _, _) = SeedTestData(context);

        // Caller thuộc Tenant A nhưng DepartmentId là null
        _currentUserServiceMock.Setup(x => x.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantA.Id);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns((Guid?)null);

        var handler = new AllocateUserStorageQuotaCommandHandler(
            context,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        var command = new AllocateUserStorageQuotaCommand(deptA.Id, Guid.NewGuid(), 1024);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Bạn không có quyền quản lý hạn mức của phòng ban này.");
    }

    [Fact]
    public async Task AllocateUserQuota_WhenUserDoesNotBelongToDepartment_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenantA, _, deptA, _, _) = SeedTestData(context);

        var deptA2 = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Phòng Kế toán Alpha",
            Code = "ACC-A",
            StorageQuotaBytes = 20L * 1024 * 1024 * 1024,
            IsActive = true
        };
        // User thuộc deptA2
        var userInOtherDept = new User(tenantA.Id, deptA2.Id, "user.other@alpha.com", "hash", "Thi", "Tran", null);
        context.Departments.Add(deptA2);
        context.Users.Add(userInOtherDept);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.Role).Returns("TenantAdmin");
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantA.Id);

        var handler = new AllocateUserStorageQuotaCommandHandler(
            context,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        // Cố tình gán quota của DeptA cho User thuộc DeptA2
        var command = new AllocateUserStorageQuotaCommand(deptA.Id, userInOtherDept.Id, 1024);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Người dùng không thuộc phòng ban được chỉ định.");
    }

    [Fact]
    public async Task GetDepartmentUsersQuota_WhenAccessingOtherTenantDepartment_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenantA, _, _, deptB, _) = SeedTestData(context);

        // Caller là nhân viên của Tenant A
        _currentUserServiceMock.Setup(x => x.Role).Returns("Employee");
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantA.Id);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns((Guid?)null);

        var handler = new GetDepartmentUsersStorageQuotaQueryHandler(
            context,
            _currentUserServiceMock.Object);

        var query = new GetDepartmentUsersStorageQuotaQuery(deptB.Id);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Phòng ban không thuộc tổ chức của bạn.");
    }

    [Fact]
    public async Task GetDepartmentUsersQuota_WhenCallerHasNoDepartmentId_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenantA, _, deptA, _, _) = SeedTestData(context);

        // Caller thuộc Tenant A nhưng DepartmentId null
        _currentUserServiceMock.Setup(x => x.Role).Returns("Employee");
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenantA.Id);
        _currentUserServiceMock.Setup(x => x.DepartmentId).Returns((Guid?)null);

        var handler = new GetDepartmentUsersStorageQuotaQueryHandler(
            context,
            _currentUserServiceMock.Object);

        var query = new GetDepartmentUsersStorageQuotaQuery(deptA.Id);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Bạn không có quyền xem thông tin hạn mức của phòng ban này.");
    }

    [Fact]
    public void CreateEmployeeCommandValidator_WhenStorageQuotaNegative_ShouldHaveValidationError()
    {
        // Arrange
        var validator = new CreateEmployeeCommandValidator();
        var command = new CreateEmployeeCommand
        {
            DepartmentId = Guid.NewGuid(),
            Email = "emp@test.com",
            FirstName = "Van",
            LastName = "Nguyen",
            StorageQuotaBytes = -500
        };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.StorageQuotaBytes)
            .WithErrorMessage("Hạn mức lưu trữ không được là số âm.");
    }

    [Fact]
    public void SetupDepartmentCommandValidator_WhenCodeContainsInvalidChars_ShouldHaveValidationError()
    {
        // Arrange
        var validator = new SetupDepartmentCommandValidator();
        var command = new SetupDepartmentCommand
        {
            Name = "Phòng IT",
            Code = "IT DEPT!", // Chứa khoảng trắng và dấu !
            Description = "Mô tả",
            AdminPersonalQuotaGB = 1
        };

        // Act
        var result = validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Code);
    }

    [Fact]
    public async Task SetupDepartment_WhenAdminQuotaAssigned_ShouldSucceedAndDistributeQuota()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = new Tenant(Guid.NewGuid(), "Tenant Test", "TEST", 100L * 1024 * 1024 * 1024);
        context.Tenants.Add(tenant);

        var deptAdminRole = new Role(Guid.NewGuid(), "DepartmentAdmin", "Trưởng phòng");
        context.Roles.Add(deptAdminRole);

        var user = new User(tenant.Id, null, "admin@test.com", "hash", "Admin", "User", null);
        user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, deptAdminRole.Id) { Role = deptAdminRole, User = user });
        context.Users.Add(user);

        // Bản ghi QuotaBytes trong ví tạm được TenantAdmin cấp 10 GB
        var adminQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            QuotaBytes = 10L * 1024 * 1024 * 1024,
            UsedBytes = 0
        };
        context.UserStorageQuotas.Add(adminQuota);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(user.Id);
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenant.Id);

        var handler = new SetupDepartmentCommandHandler(context, _currentUserServiceMock.Object);
        var command = new SetupDepartmentCommand
        {
            Name = "Bo béo",
            Code = "BO",
            Description = "Phòng ban test",
            AdminPersonalQuotaGB = 4
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.StorageQuotaBytes.Should().Be(10L * 1024 * 1024 * 1024); // 10 GB
        result.Value.Name.Should().Be("Bo béo");
        result.Value.Code.Should().Be("BO");

        // Trưởng phòng đã được cập nhật
        user.DepartmentId.Should().Be(result.Value.Id);
        adminQuota.QuotaBytes.Should().Be(4L * 1024 * 1024 * 1024); // Giữ lại 4 GB
        adminQuota.DepartmentId.Should().Be(result.Value.Id);
    }

    [Fact]
    public async Task GetDepartmentSetupInfo_WhenAdminQuotaAssigned_ShouldReturnAssignedQuota()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = new Tenant(Guid.NewGuid(), "Tenant Test", "TEST", 100L * 1024 * 1024 * 1024);
        context.Tenants.Add(tenant);

        var deptAdminRole = new Role(Guid.NewGuid(), "DepartmentAdmin", "Trưởng phòng");
        context.Roles.Add(deptAdminRole);

        var user = new User(tenant.Id, null, "admin@test.com", "hash", "Admin", "User", null);
        user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, deptAdminRole.Id) { Role = deptAdminRole, User = user });
        context.Users.Add(user);

        var adminQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            QuotaBytes = 10L * 1024 * 1024 * 1024,
            UsedBytes = 0
        };
        context.UserStorageQuotas.Add(adminQuota);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(user.Id);
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenant.Id);

        var handler = new CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentSetupInfo.GetDepartmentSetupInfoQueryHandler(
            context, _currentUserServiceMock.Object);

        // Act
        var result = await handler.Handle(
            new CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentSetupInfo.GetDepartmentSetupInfoQuery(),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AssignedQuotaGB.Should().Be(10.0);
        result.Value.AssignedQuotaBytes.Should().Be(10L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task SetupDepartment_WhenAdminHasNoQuota_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenant = new Tenant(Guid.NewGuid(), "Tenant Test", "TEST", 100L * 1024 * 1024 * 1024);
        context.Tenants.Add(tenant);

        var deptAdminRole = new Role(Guid.NewGuid(), "DepartmentAdmin", "Trưởng phòng");
        context.Roles.Add(deptAdminRole);

        var user = new User(tenant.Id, null, "admin@test.com", "hash", "Admin", "User", null);
        user.UserRoles.Add(new UserRole(Guid.NewGuid(), user.Id, deptAdminRole.Id) { Role = deptAdminRole, User = user });
        context.Users.Add(user);

        var zeroQuota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            QuotaBytes = 0,
            UsedBytes = 0
        };
        context.UserStorageQuotas.Add(zeroQuota);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(user.Id);
        _currentUserServiceMock.Setup(x => x.TenantId).Returns(tenant.Id);

        var handler = new SetupDepartmentCommandHandler(context, _currentUserServiceMock.Object);
        var command = new SetupDepartmentCommand
        {
            Name = "Bo béo",
            Code = "BO",
            Description = "Phòng ban test",
            AdminPersonalQuotaGB = 2
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("chưa được cấp hạn mức lưu trữ");
    }
}
