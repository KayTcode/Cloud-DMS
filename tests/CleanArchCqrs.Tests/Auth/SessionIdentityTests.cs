using System.IdentityModel.Tokens.Jwt;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Infrastructure.Authentication;
using CleanArchCqrs.Infrastructure.Services;
using Microsoft.Extensions.Options;
using CleanArchCqrs.API.Services;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Tests.Common;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CleanArchCqrs.Tests.Auth;

public class SessionIdentityTests
{
    [Fact]
    public void ValidatedBearerPrincipalRetainsSessionIdentity()
    {
        var secret = new string('x', 64);
        var jwt = new JwtTokenService(Options.Create(new JwtOptions { SecretKey = secret, Issuer = "test", Audience = "test", ExpirationMinutes = 15 }));
        var sessionId = Guid.NewGuid();
        var user = new User(null, null, "test@example.test", "hash", "Test", "User", null);
        var token = jwt.GenerateAccessToken(user, "Employee", Array.Empty<string>(), sessionId);
        var principal = new JwtSecurityTokenHandler().ValidateToken(token.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "test", ValidAudience = "test", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateLifetime = true
        }, out _);
        var current = new CurrentUserService(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
        Assert.Equal(user.Id, current.UserId);
        Assert.Equal(sessionId, current.SessionId);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void MissingOrInvalidSidHasNoSession(string? value)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(value is null ? Array.Empty<Claim>() : new[] { new Claim("sid", value) }, "test"));
        Assert.Null(new CurrentUserService(new HttpContextAccessor { HttpContext = context }).SessionId);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task ResolverRejectsMissingInventedExpiredAndRevokedSessions(bool sidPresent, bool expired, bool revoked)
    {
        using var db = TestDbContextFactory.Create();
        var current = new Mock<ICurrentUserService>();
        var id = Guid.NewGuid(); var sid = Guid.NewGuid();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(id);
        current.SetupGet(x => x.SessionId).Returns(sidPresent ? sid : null);
        if (expired || revoked)
        {
            var token = new RefreshToken(sid, id, "hash", DateTime.UtcNow.AddDays(expired ? -1 : 7), null);
            if (revoked) token.Revoke();
            db.RefreshTokens.Add(token); await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new FileAccessContextResolver(current.Object, db, Mock.Of<IAccessSnapshotStore>()).ResolveContextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResolverUsesAuthoritativeTenantAndFailsClosedOnRedisError()
    {
        using var db = TestDbContextFactory.Create();
        var id = Guid.NewGuid(); var sid = Guid.NewGuid(); var tenant = Guid.NewGuid();
        db.RefreshTokens.Add(new RefreshToken(sid, id, "hash", DateTime.UtcNow.AddDays(7), null)); await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true); current.SetupGet(x => x.UserId).Returns(id);
        current.SetupGet(x => x.SessionId).Returns(sid); current.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        var snapshots = new Mock<IAccessSnapshotStore>();
        snapshots.Setup(x => x.ReadAsync(It.IsAny<ActorIdentity>(), It.IsAny<AuthorizationTarget>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AccessEvaluationInput(
            new(id, tenant, null, true, 1, new HashSet<string>(), new HashSet<string>()), new(sid, id, true, DateTimeOffset.UtcNow.AddDays(7), 1, true), null,
            Array.Empty<ShareGrant>(), new[] { new ScopeAccessSnapshot("dms:scope:global", 0, false, true) }, true, DateTimeOffset.UtcNow, "generation", new("none", null)));
        var resolver = new FileAccessContextResolver(current.Object, db, snapshots.Object);
        Assert.Equal(tenant, (await resolver.ResolveContextAsync(CancellationToken.None)).TenantId);
        snapshots.Setup(x => x.ReadAsync(It.IsAny<ActorIdentity>(), It.IsAny<AuthorizationTarget>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SecurityUnavailableException());
        await Assert.ThrowsAsync<SecurityUnavailableException>(() => resolver.ResolveContextAsync(CancellationToken.None));
    }
    [Fact]
    public void AccessTokenMustNotInventSessionIdentity()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            SecretKey = new string('x', 64), Issuer = "test", Audience = "test", ExpirationMinutes = 15
        }));
        var user = new User(Guid.NewGuid(), null, "test@example.test", "hash", "Test", "User", null);
        Assert.Throws<ArgumentException>(() => service.GenerateAccessToken(user, "Employee", Array.Empty<string>()));
    }
    [Fact] public async Task ReceiptContextAllowsPendingButNeverBlockedSessionOrIncompleteSnapshot()
    {
        using var db=TestDbContextFactory.Create();var id=Guid.NewGuid();var sid=Guid.NewGuid();var tenant=Guid.NewGuid();db.RefreshTokens.Add(new(sid,id,"hash",DateTime.UtcNow.AddHours(1),null));await db.SaveChangesAsync();
        var current=new Mock<ICurrentUserService>();current.SetupGet(c=>c.IsAuthenticated).Returns(true);current.SetupGet(c=>c.UserId).Returns(id);current.SetupGet(c=>c.SessionId).Returns(sid);
        var input=new AccessEvaluationInput(new(id,tenant,null,true,1,new HashSet<string>(),new HashSet<string>()),new(sid,id,true,DateTimeOffset.UtcNow.AddHours(1),1,true),null,Array.Empty<ShareGrant>(),new[]{new ScopeAccessSnapshot("dms:scope:global",1,true,true)},true,DateTimeOffset.UtcNow,"g",new("none",null));
        var snapshots=new Mock<IAccessSnapshotStore>();snapshots.Setup(s=>s.ReadAsync(It.IsAny<ActorIdentity>(),It.IsAny<AuthorizationTarget>(),It.IsAny<CancellationToken>())).Returns(()=>Task.FromResult(input));var resolver=new FileAccessContextResolver(current.Object,db,snapshots.Object);
        Assert.Equal(tenant,(await resolver.ResolveContextForMutationReceiptAsync(default)).TenantId);await Assert.ThrowsAsync<SecurityUnavailableException>(()=>resolver.ResolveContextAsync(default));
        input=input with{Session=input.Session! with{IsActive=false}};await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>resolver.ResolveContextForMutationReceiptAsync(default));
        input=input with{SnapshotsComplete=false};await Assert.ThrowsAsync<SecurityUnavailableException>(()=>resolver.ResolveContextForMutationReceiptAsync(default));
    }}
