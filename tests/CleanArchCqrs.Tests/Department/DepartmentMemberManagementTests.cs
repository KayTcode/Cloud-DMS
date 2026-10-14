using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.DeleteDepartmentEmployee;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Commands.UpdateDepartmentEmployee;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentMemberById;
using CleanArchCqrs.Application.Features.DepartmentAdmin.Queries.GetDepartmentMembers;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.DepartmentAdminTests;

public class DepartmentMemberManagementTests
{
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();

    [Fact]
    public async Task GetDepartmentMembers_WhenAuthorized_ShouldReturnPaginatedMembersWithQuota()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var deptAdminId = Guid.NewGuid();

        var department = new CleanArchCqrs.Domain.Entities.Department
        {
            Id = departmentId,
            TenantId = tenantId,
            Name = "Phòng Công nghệ",
            Code = "IT",
            StorageQuotaBytes = 100L * 1024 * 1024 * 1024, // 100 GB
            IsActive = true
        };
        context.Departments.Add(department);

        var employee1 = new User(tenantId, departmentId, "dev1@company.com", "hash", "An", "Nguyen", "0901234567");
        var employee2 = new User(tenantId, departmentId, "dev2@company.com", "hash", "Binh", "Tran", "0907654321");
        context.Users.AddRange(employee1, employee2);

        var quota1 = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = employee1.Id,
            QuotaBytes = 10L * 1024 * 1024 * 1024, // 10 GB
            UsedBytes = 2L * 1024 * 1024 * 1024   // 2 GB (20%)
        };
        var quota2 = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = employee2.Id,
            QuotaBytes = 5L * 1024 * 1024 * 1024,
            UsedBytes = 0
        };
        context.UserStorageQuotas.AddRange(quota1, quota2);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(s => s.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(s => s.DepartmentId).Returns(departmentId);
        _currentUserServiceMock.Setup(s => s.TenantId).Returns(tenantId);
        _currentUserServiceMock.Setup(s => s.UserId).Returns(deptAdminId);

        var handler = new GetDepartmentMembersWithPaginationQueryHandler(context, _currentUserServiceMock.Object);

        // Act
        var query = new GetDepartmentMembersWithPaginationQuery
        {
            DepartmentId = departmentId,
            PageNumber = 1,
            PageSize = 10
        };
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Items.Should().HaveCount(2);

        var member1 = result.Value.Items.First(m => m.Id == employee1.Id);
        member1.FullName.Should().Be("An Nguyen");
        member1.StorageQuotaBytes.Should().Be(10L * 1024 * 1024 * 1024);
        member1.StorageUsedBytes.Should().Be(2L * 1024 * 1024 * 1024);
        member1.UsagePercentage.Should().Be(20.0);
    }

    [Fact]
    public async Task GetDepartmentMembers_WhenCallerFromAnotherDepartment_ShouldReject()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var departmentId = Guid.NewGuid();
        var otherDeptId = Guid.NewGuid();

        var department = new CleanArchCqrs.Domain.Entities.Department
        {
            Id = departmentId,
            TenantId = Guid.NewGuid(),
            Name = "Phòng Kế toán",
            Code = "ACC",
            IsActive = true
        };
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(s => s.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(s => s.DepartmentId).Returns(otherDeptId); // Khác phòng ban

        var handler = new GetDepartmentMembersWithPaginationQueryHandler(context, _currentUserServiceMock.Object);

        // Act
        var query = new GetDepartmentMembersWithPaginationQuery { DepartmentId = departmentId };
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("chỉ có quyền xem danh sách thành viên trong phòng ban của mình");
    }

    [Fact]
    public async Task UpdateDepartmentEmployee_WhenValid_ShouldUpdateInfoAndQuota()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var deptAdminId = Guid.NewGuid();

        var department = new CleanArchCqrs.Domain.Entities.Department
        {
            Id = departmentId,
            TenantId = tenantId,
            Name = "Phòng Kinh Doanh",
            Code = "SALES",
            StorageQuotaBytes = 50L * 1024 * 1024 * 1024, // 50 GB
            IsActive = true
        };
        context.Departments.Add(department);

        var employee = new User(tenantId, departmentId, "sales@company.com", "hash", "Cuong", "Le", "0912345678");
        context.Users.Add(employee);

        var quota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            QuotaBytes = 2L * 1024 * 1024 * 1024,
            UsedBytes = 1L * 1024 * 1024 * 1024
        };
        context.UserStorageQuotas.Add(quota);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(s => s.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(s => s.DepartmentId).Returns(departmentId);
        _currentUserServiceMock.Setup(s => s.TenantId).Returns(tenantId);
        _currentUserServiceMock.Setup(s => s.UserId).Returns(deptAdminId);

        var handler = new UpdateDepartmentEmployeeCommandHandler(context, _currentUserServiceMock.Object);

        // Act
        var command = new UpdateDepartmentEmployeeCommand
        {
            DepartmentId = departmentId,
            UserId = employee.Id,
            FirstName = "Cuong Updated",
            LastName = "Le",
            PhoneNumber = "0988888888",
            IsActive = true,
            StorageQuotaBytes = 10L * 1024 * 1024 * 1024 // Tăng lên 10 GB
        };
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.FirstName.Should().Be("Cuong Updated");
        result.Value.PhoneNumber.Should().Be("0988888888");
        result.Value.StorageQuotaBytes.Should().Be(10L * 1024 * 1024 * 1024);

        var updatedQuota = await context.UserStorageQuotas.FirstAsync(q => q.UserId == employee.Id);
        updatedQuota.QuotaBytes.Should().Be(10L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task UpdateDepartmentEmployee_WhenRequestedQuotaExceedsDepartmentCapacity_ShouldReject()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var departmentId = Guid.NewGuid();
        var department = new CleanArchCqrs.Domain.Entities.Department
        {
            Id = departmentId,
            TenantId = Guid.NewGuid(),
            Name = "Phòng Marketing",
            Code = "MKT",
            StorageQuotaBytes = 10L * 1024 * 1024 * 1024, // Phòng chỉ có 10 GB
            IsActive = true
        };
        context.Departments.Add(department);

        var employee = new User(department.TenantId, departmentId, "mkt@company.com", "hash", "Lan", "Pham", null);
        context.Users.Add(employee);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(s => s.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(s => s.DepartmentId).Returns(departmentId);

        var handler = new UpdateDepartmentEmployeeCommandHandler(context, _currentUserServiceMock.Object);

        // Act: Đòi cấp 20 GB trong khi phòng chỉ có 10 GB
        var command = new UpdateDepartmentEmployeeCommand
        {
            DepartmentId = departmentId,
            UserId = employee.Id,
            FirstName = "Lan",
            LastName = "Pham",
            StorageQuotaBytes = 20L * 1024 * 1024 * 1024
        };
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("vượt quá dung lượng khả dụng của phòng ban");
    }

    [Fact]
    public async Task DeleteDepartmentEmployee_WhenValid_ShouldDeactivateAndRevokeTokensAndReclaimQuota()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var departmentId = Guid.NewGuid();
        var deptAdminId = Guid.NewGuid();

        var department = new CleanArchCqrs.Domain.Entities.Department
        {
            Id = departmentId,
            TenantId = Guid.NewGuid(),
            Name = "Phòng Kỹ Thuật",
            Code = "TECH",
            StorageQuotaBytes = 100L * 1024 * 1024 * 1024,
            IsActive = true
        };
        context.Departments.Add(department);

        var employee = new User(department.TenantId, departmentId, "tech@company.com", "hash", "Tuan", "Hoang", null);
        context.Users.Add(employee);

        var quota = new UserStorageQuota
        {
            Id = Guid.NewGuid(),
            UserId = employee.Id,
            QuotaBytes = 15L * 1024 * 1024 * 1024, // 15 GB
            UsedBytes = 3L * 1024 * 1024 * 1024    // 3 GB dùng
        };
        context.UserStorageQuotas.Add(quota);

        var token = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            employee.Id,
            "sample_token_hash",
            DateTime.UtcNow.AddDays(7),
            null);
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        _currentUserServiceMock.Setup(s => s.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(s => s.DepartmentId).Returns(departmentId);
        _currentUserServiceMock.Setup(s => s.UserId).Returns(deptAdminId);

        var handler = new DeleteDepartmentEmployeeCommandHandler(context, _currentUserServiceMock.Object);

        // Act
        var command = new DeleteDepartmentEmployeeCommand
        {
            DepartmentId = departmentId,
            UserId = employee.Id,
            RemoveFromDepartmentOnly = false
        };
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updatedEmployee = await context.Users.FirstAsync(u => u.Id == employee.Id);
        updatedEmployee.IsActive.Should().BeFalse();

        // Refresh token must be revoked/removed
        var tokens = await context.RefreshTokens.Where(rt => rt.UserId == employee.Id).ToListAsync();
        tokens.Should().BeEmpty();

        // Quota should be reclaimed down to UsedBytes (3 GB)
        var updatedQuota = await context.UserStorageQuotas.FirstAsync(q => q.UserId == employee.Id);
        updatedQuota.QuotaBytes.Should().Be(3L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task DeleteDepartmentEmployee_WhenAttemptingToDeleteSelf_ShouldReject()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var departmentId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        _currentUserServiceMock.Setup(s => s.Role).Returns("DepartmentAdmin");
        _currentUserServiceMock.Setup(s => s.DepartmentId).Returns(departmentId);
        _currentUserServiceMock.Setup(s => s.UserId).Returns(adminId);

        var department = new CleanArchCqrs.Domain.Entities.Department
        {
            Id = departmentId,
            TenantId = Guid.NewGuid(),
            Name = "Phòng Nhân Sự",
            Code = "HR",
            IsActive = true
        };
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        var handler = new DeleteDepartmentEmployeeCommandHandler(context, _currentUserServiceMock.Object);

        // Act: Admin cố tự xóa chính mình
        var command = new DeleteDepartmentEmployeeCommand
        {
            DepartmentId = departmentId,
            UserId = adminId
        };
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("không thể tự xóa hoặc vô hiệu hóa tài khoản của chính mình");
    }
}
