using CleanArchCqrs.Application.Common.Behaviors;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.Commands.Login;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.Auth;

public class UserLoginAndAuditTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock = new();
    private readonly Mock<IUserAuthorizationRepository> _userAuthRepoMock = new();
    private readonly Mock<ILogger<LoggingBehavior<LoginCommand, string>>> _loggingBehaviorLoggerMock = new();

    public UserLoginAndAuditTests()
    {
        _passwordHasherMock.Setup(x => x.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _jwtTokenServiceMock.Setup(x => x.GenerateAccessToken(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Guid?>()))
            .Returns(new JwtTokenResult("token", DateTime.UtcNow.AddMinutes(15)));
        _userAuthRepoMock.Setup(x => x.GetRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("Employee");
        _userAuthRepoMock.Setup(x => x.GetPermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    private static IAuthSessionService CreateSessions() { var mock = new Mock<IAuthSessionService>(); mock.Setup(x => x.IssueAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new JwtTokenResult("token", DateTime.UtcNow.AddMinutes(15))); return mock.Object; }
    [Fact]
    public void MarkLogin_ShouldUpdateLastLoginAtTimestamp()
    {
        // Arrange
        var user = new User(Guid.NewGuid(), null, "test@test.com", "hash", "Test", "User", null);
        user.LastLoginAt.Should().BeNull();

        // Act
        user.MarkLogin();

        // Assert
        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LoginCommandHandler_OnSuccessfulLogin_ShouldPersistLastLoginAt()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "john@test.com", "hashed_password", "John", "Doe", null);
        user.EmailConfirmed = true;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        _userRepositoryMock.Setup(x => x.GetByEmailAsync("john@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new LoginCommandHandler(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            _jwtTokenServiceMock.Object,
            _userAuthRepoMock.Object,
            context, CreateSessions());

        // Act
        var result = await handler.Handle(new LoginCommand("john@test.com", "password123"), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var reloadedUser = await context.Users.FirstAsync(u => u.Id == user.Id);
        reloadedUser.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LoggingBehavior_ShouldExecuteDelegateAndMeasureTime()
    {
        // Arrange
        var behavior = new LoggingBehavior<LoginCommand, string>(_loggingBehaviorLoggerMock.Object);
        var command = new LoginCommand("test@email.com", "secret");

        RequestHandlerDelegate<string> next = (cancellationToken) => Task.FromResult("SUCCESS");

        // Act
        var response = await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        response.Should().Be("SUCCESS");
    }
}
