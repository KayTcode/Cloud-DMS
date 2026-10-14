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
        _userAuthRepoMock.Setup(x => x.GetRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Employee");
        _userAuthRepoMock.Setup(x => x.GetPermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "file.read" });
        _jwtTokenServiceMock.Setup(x => x.GenerateAccessToken(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new JwtTokenResult("new-jwt-access-token", DateTime.UtcNow.AddMinutes(15)));
    }

    [Fact]
    public async Task Handle_WhenValidRefreshTokenProvided_ShouldRotateTokensSuccessfully()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "user@test.com", "hash", "Jane", "Doe", null);
        context.Users.Add(user);

        var rawToken = "valid-raw-refresh-token-12345";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

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
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.AccessToken.Should().Be("new-jwt-access-token");
        result.Value.RefreshToken.Should().NotBe(rawToken); // Rotated to new raw token

        // Verify old token was revoked in DB
        var oldToken = await context.RefreshTokens.FirstAsync(t => t.Id == tokenRecord.Id);
        oldToken.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WhenTokenExpired_ShouldThrowUnauthorized()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "user@test.com", "hash", "Jane", "Doe", null);
        context.Users.Add(user);

        var rawToken = "expired-token-12345";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        // Expired yesterday
        var tokenRecord = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            tokenHash,
            DateTime.UtcNow.AddDays(-1),
            null);
        context.RefreshTokens.Add(tokenRecord);
        await context.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(context, _userAuthRepoMock.Object, _jwtTokenServiceMock.Object);

        // Act & Assert
        var act = () => handler.Handle(new RefreshTokenCommand(rawToken), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*hết hạn*");
    }

    [Fact]
    public async Task Handle_WhenRevokedTokenReused_ShouldRevokeAllUserTokensAndThrowSecurityException()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var user = new User(Guid.NewGuid(), null, "victim@test.com", "hash", "Jane", "Doe", null);
        context.Users.Add(user);

        var stolenRawToken = "stolen-raw-token";
        var stolenTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stolenRawToken))).ToLowerInvariant();

        // Stolen token is already revoked
        var revokedToken = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            stolenTokenHash,
            DateTime.UtcNow.AddDays(7),
            null);
        revokedToken.Revoke("rotated_previously");

        // User also has another active session token
        var activeToken = new Domain.Entities.RefreshToken(
            Guid.NewGuid(),
            user.Id,
            "another_active_hash",
            DateTime.UtcNow.AddDays(7),
            null);

        context.RefreshTokens.AddRange(revokedToken, activeToken);
        await context.SaveChangesAsync();

        var handler = new RefreshTokenCommandHandler(context, _userAuthRepoMock.Object, _jwtTokenServiceMock.Object);

        // Act & Assert: Attacker tries to use the already revoked token
        var act = () => handler.Handle(new RefreshTokenCommand(stolenRawToken), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*tái sử dụng*");

        // Assert: The other active session token MUST now also be revoked
        var recheckedActiveToken = await context.RefreshTokens.FirstAsync(t => t.Id == activeToken.Id);
        recheckedActiveToken.RevokedAt.Should().NotBeNull();
        recheckedActiveToken.ReplacedByTokenHash.Should().Be("token_reuse_detected");
    }
}
