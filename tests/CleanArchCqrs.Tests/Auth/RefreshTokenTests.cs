using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.Commands.RefreshToken;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.Auth;

public class RefreshTokenTests
{
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock = new();
    private readonly Mock<IUserAuthorizationRepository> _userAuthRepoMock = new();

    public RefreshTokenTests()
    {
        _jwtTokenServiceMock.Setup(x => x.GenerateAccessToken(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new JwtTokenResult("new-jwt-access-token", DateTime.UtcNow.AddMinutes(15)));
        _userAuthRepoMock.Setup(x => x.GetRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Employee");
        _userAuthRepoMock.Setup(x => x.GetPermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    [Fact]
    public async Task Handle_WhenValidRefreshTokenProvided_ShouldRotateTokenAndReturnNewTokens()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "test@test.com", "hash", "John", "Doe", null);
        context.Users.Add(user);

        var rawToken = "raw-refresh-token-123";
        using var sha256 = SHA256.Create();
        var tokenHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(rawToken)));
        var tokenRecord = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            tokenHash,
            DateTime.UtcNow.AddDays(7),
            null);
        context.RefreshTokens.Add(tokenRecord);
        await context.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(context, _userAuthRepoMock.Object, _jwtTokenServiceMock.Object);

        // Act
        var result = await handler.Handle(new RefreshTokenCommand(rawToken), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.AccessToken.Should().Be("new-jwt-access-token");
        result.Value.RefreshToken.Should().NotBe(rawToken);

        // Verify old token was revoked in DB
        var oldToken = await context.RefreshTokens.FirstAsync(t => t.Id == tokenRecord.Id);
        oldToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WhenTokenExpired_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "test@test.com", "hash", "John", "Doe", null);
        context.Users.Add(user);

        var rawToken = "expired-raw-token";
        using var sha256 = SHA256.Create();
        var tokenHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(rawToken)));
        var tokenRecord = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            tokenHash,
            DateTime.UtcNow.AddDays(-1),
            null);
        context.RefreshTokens.Add(tokenRecord);
        await context.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(context, _userAuthRepoMock.Object, _jwtTokenServiceMock.Object);

        // Act
        var result = await handler.Handle(new RefreshTokenCommand(rawToken), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("hết hạn");
    }

    [Fact]
    public async Task Handle_WhenRevokedTokenReused_ShouldReturnFailure()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "victim@test.com", "hash", "Jane", "Doe", null);
        context.Users.Add(user);

        var stolenRawToken = "stolen-raw-token";
        using var sha256 = SHA256.Create();
        var stolenTokenHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(stolenRawToken)));

        var revokedToken = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            stolenTokenHash,
            DateTime.UtcNow.AddDays(7),
            null);
        revokedToken.Revoke("rotated_previously");

        context.RefreshTokens.Add(revokedToken);
        await context.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(context, _userAuthRepoMock.Object, _jwtTokenServiceMock.Object);

        // Act
        var result = await handler.Handle(new RefreshTokenCommand(stolenRawToken), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }
}
