using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.TenantAdmin.Commands.CreateDepartmentAdmin;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.TenantAdmin;

public class CreateDepartmentAdminCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IEmailService> _emailServiceMock = new();

    public CreateDepartmentAdminCommandHandlerTests()
    {
        _passwordHasherMock
            .Setup(p => p.HashPassword(It.IsAny<string>()))
            .Returns("hashed_temporary_password");
    }

    private static (Tenant tenant, Role deptAdminRole) SeedCommonData(Infrastructure.Persistence.AppDbContext context)
    {
        var tenant = new Tenant(Guid.NewGuid(), "Acme Corp", "ACME", 20L * 1024 * 1024 * 1024);
        context.Tenants.Add(tenant);

        var deptAdminRole = new Role(Guid.NewGuid(), "DepartmentAdmin", "Quản trị viên phòng ban");
        context.Roles.Add(deptAdminRole);

        context.SaveChanges();
        return (tenant, deptAdminRole);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesUserWithIsActiveFalse_GeneratesInvitationToken_AndSendsEmail()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenant, _) = SeedCommonData(context);

        var handler = new CreateDepartmentAdminCommandHandler(
            context,
            _passwordHasherMock.Object,
            _emailServiceMock.Object);

        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenant.Id,
            DepartmentId = null,
            Email = "lead@acme.com",
            FirstName = "An",
            LastName = "Nguyen",
            StorageQuotaBytes = 5L * 1024 * 1024 * 1024
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Email.Should().Be("lead@acme.com");
        result.Value.IsActive.Should().BeFalse();

        // Kiểm tra User trong CSDL
        var userInDb = await context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == "lead@acme.com");

        userInDb.Should().NotBeNull();
        userInDb!.IsActive.Should().BeFalse();
        userInDb.EmailConfirmed.Should().BeFalse();
        userInDb.PasswordHash.Should().Be("hashed_temporary_password");
        userInDb.UserRoles.Should().Contain(ur => ur.Role.Name == "DepartmentAdmin");

        // Kiểm tra Quota
        var quotaInDb = await context.UserStorageQuotas.FirstOrDefaultAsync(q => q.UserId == userInDb.Id);
        quotaInDb.Should().NotBeNull();
        quotaInDb!.QuotaBytes.Should().Be(5L * 1024 * 1024 * 1024);

        // Kiểm tra EmailVerificationToken
        var tokenInDb = await context.EmailVerificationTokens.FirstOrDefaultAsync(t => t.UserId == userInDb.Id);
        tokenInDb.Should().NotBeNull();
        tokenInDb!.TokenType.Should().Be("DepartmentAdminInvitation");
        tokenInDb.IsUsed.Should().BeFalse();
        tokenInDb.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddHours(47));

        // Kiểm tra AuditLog
        var auditLogInDb = await context.AuditLogs.FirstOrDefaultAsync(a => a.UserId == userInDb.Id);
        auditLogInDb.Should().NotBeNull();
        auditLogInDb!.Action.Should().Be("InviteDepartmentAdminWithQuota");

        // Kiểm tra Email đã được gửi
        _emailServiceMock.Verify(m => m.SendDepartmentAdminInvitationAsync(
            "lead@acme.com",
            "An Nguyen",
            tenant.Name,
            null,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SoftDeletedUserExists_AllowsRecreation_ReleasesOldEmail_AndCreatesNewAccount()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenant, _) = SeedCommonData(context);

        // Tạo sẵn user đã bị xóa mềm (IsActive = false)
        var oldUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "lead@acme.com",
            FirstName = "Old",
            LastName = "User",
            PasswordHash = "old_hash",
            TenantId = tenant.Id,
            IsActive = false
        };
        context.Users.Add(oldUser);
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentAdminCommandHandler(
            context,
            _passwordHasherMock.Object,
            _emailServiceMock.Object);

        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenant.Id,
            DepartmentId = null,
            Email = "lead@acme.com",
            FirstName = "New",
            LastName = "Admin",
            StorageQuotaBytes = 2L * 1024 * 1024 * 1024
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Kiểm tra bản ghi cũ đã được đổi tên để giải phóng email
        var releasedOldUser = await context.Users.FirstOrDefaultAsync(u => u.Id == oldUser.Id);
        releasedOldUser.Should().NotBeNull();
        releasedOldUser!.Email.Should().StartWith("lead@acme.com__deleted_");

        // Kiểm tra bản ghi mới được tạo thành công với email gốc
        var newUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "lead@acme.com");
        newUser.Should().NotBeNull();
        newUser!.Id.Should().NotBe(oldUser.Id);
        newUser.FirstName.Should().Be("New");
        newUser.IsActive.Should().BeFalse();

        // Token mời được tạo cho newUser
        var token = await context.EmailVerificationTokens.FirstOrDefaultAsync(t => t.UserId == newUser.Id);
        token.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ActiveUserExists_ReturnsDuplicateEmailError()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenant, _) = SeedCommonData(context);

        // User đang hoạt động (IsActive = true)
        var activeUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "active@acme.com",
            FirstName = "Active",
            LastName = "User",
            PasswordHash = "hash",
            TenantId = tenant.Id,
            IsActive = true
        };
        context.Users.Add(activeUser);
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentAdminCommandHandler(
            context,
            _passwordHasherMock.Object,
            _emailServiceMock.Object);

        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenant.Id,
            DepartmentId = null,
            Email = "active@acme.com",
            FirstName = "New",
            LastName = "Admin"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Email 'active@acme.com' đã tồn tại trong hệ thống.");

        _emailServiceMock.Verify(m => m.SendDepartmentAdminInvitationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DepartmentAlreadyHasAdmin_ReturnsError()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenant, deptAdminRole) = SeedCommonData(context);

        var department = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Kỹ thuật",
            Code = "TECH",
            QuotaBytes = 10L * 1024 * 1024 * 1024,
            IsActive = true
        };
        context.Departments.Add(department);

        var existingAdmin = new User
        {
            Id = Guid.NewGuid(),
            Email = "tech.lead@acme.com",
            FirstName = "Tech",
            LastName = "Lead",
            PasswordHash = "hash",
            TenantId = tenant.Id,
            DepartmentId = department.Id,
            IsActive = true
        };
        existingAdmin.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = existingAdmin.Id,
            RoleId = deptAdminRole.Id,
            User = existingAdmin,
            Role = deptAdminRole
        });
        context.Users.Add(existingAdmin);
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentAdminCommandHandler(
            context,
            _passwordHasherMock.Object,
            _emailServiceMock.Object);

        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenant.Id,
            DepartmentId = department.Id,
            Email = "another.lead@acme.com",
            FirstName = "Another",
            LastName = "Lead"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("hiện đã có Trưởng phòng quản lý");
    }

    [Fact]
    public async Task Handle_QuotaExceedsAvailableInTenant_ReturnsError()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenant, _) = SeedCommonData(context); // Quota 20 GB

        // Tạo phòng ban chiếm 18 GB
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Kinh doanh",
            Code = "BIZ",
            QuotaBytes = 18L * 1024 * 1024 * 1024,
            IsActive = true
        };
        context.Departments.Add(dept);
        await context.SaveChangesAsync();

        var handler = new CreateDepartmentAdminCommandHandler(
            context,
            _passwordHasherMock.Object,
            _emailServiceMock.Object);

        // Yêu cầu cấp 5 GB (trong khi tenant chỉ còn 2 GB trống)
        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenant.Id,
            DepartmentId = null,
            Email = "biz.lead@acme.com",
            FirstName = "Biz",
            LastName = "Lead",
            StorageQuotaBytes = 5L * 1024 * 1024 * 1024
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("vượt quá dung lượng khả dụng của Tổ chức");
    }

    [Fact]
    public async Task Handle_EmailServiceFails_DoesNotRollbackUserCreation()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var (tenant, _) = SeedCommonData(context);

        // Giả lập SMTP bị lỗi
        _emailServiceMock
            .Setup(m => m.SendDepartmentAdminInvitationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Mail.SmtpException("SMTP offline"));

        var handler = new CreateDepartmentAdminCommandHandler(
            context,
            _passwordHasherMock.Object,
            _emailServiceMock.Object);

        var command = new CreateDepartmentAdminCommand
        {
            TenantId = tenant.Id,
            DepartmentId = null,
            Email = "lead@acme.com",
            FirstName = "An",
            LastName = "Nguyen"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert - Handler vẫn trả về thành công vì lỗi email không được làm hỏng DB transaction
        result.IsSuccess.Should().BeTrue();

        var userInDb = await context.Users.FirstOrDefaultAsync(u => u.Email == "lead@acme.com");
        userInDb.Should().NotBeNull();
        userInDb!.IsActive.Should().BeFalse();

        var tokenInDb = await context.EmailVerificationTokens.FirstOrDefaultAsync(t => t.UserId == userInDb.Id);
        tokenInDb.Should().NotBeNull();
    }
}
