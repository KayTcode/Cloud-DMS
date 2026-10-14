using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Application.Users.Commands.RefreshToken;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace CleanArchCqrs.Tests.Auth;

public class RefreshTokenTests
{
    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public async Task RotationOnlyAcceptsActiveSessionAndUser(bool expired, bool revoked, bool activeUser, bool expectedSuccess)
    {
        using var db = TestDbContextFactory.Create();
        var user = new User(null, null, "user@example.test", "hash", "User", "Test", null) { EmailConfirmed = true, IsActive = activeUser };
        const string raw = "opaque-refresh-token";
        var token = new CleanArchCqrs.Domain.Entities.RefreshToken(Guid.NewGuid(), user.Id,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), DateTime.UtcNow.AddDays(expired ? -1 : 7), null);
        if (revoked) token.Revoke();
        db.Users.Add(user); db.RefreshTokens.Add(token); await db.SaveChangesAsync();
        var auth = new Mock<IUserAuthorizationRepository>();
        auth.Setup(x => x.GetRoleAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync("Employee");
        auth.Setup(x => x.GetPermissionsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(x => x.IssueAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JwtTokenResult("token", DateTime.UtcNow.AddMinutes(15)));
        var handler = new RefreshTokenCommandHandler(db, auth.Object, sessions.Object);
        var result = await handler.Handle(new(raw), CancellationToken.None);
        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (expectedSuccess)
        {
            Assert.NotNull(token.RevokedAt);
            Assert.NotEqual(raw, result.Value!.RefreshToken);
            Assert.Equal(2, await db.RefreshTokens.CountAsync());
            Assert.False((await handler.Handle(new(raw), CancellationToken.None)).IsSuccess);
        }
        else Assert.Equal(1, await db.RefreshTokens.CountAsync());
    }
}
