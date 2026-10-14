using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Security;
namespace CleanArchCqrs.Tests.Common;
public sealed class FileReadFixture
{
    public IFileAccessContextResolver Resolver { get; }
    public IFileAccessService Access { get; }
    public IAccessSnapshotStore Snapshots { get; }
    public FileReadFixture(AppDbContext db, Mock<ICurrentUserService> current)
    {
        var id = current.Object.UserId ?? Guid.NewGuid();
        var user = db.Users.Find(id);
        if (user is null) { user = new User(current.Object.TenantId, current.Object.DepartmentId, $"{id:N}@example.test", "hash", "A", "B", null) { Id = id }; db.Users.Add(user); }
        var session = new RefreshToken(Guid.NewGuid(), id, "fixture", DateTime.UtcNow.AddHours(1), null);
        db.RefreshTokens.Add(session); db.SaveChanges();
        current.Setup(x => x.UserId).Returns(id); current.Setup(x => x.SessionId).Returns(session.Id); current.Setup(x => x.IsAuthenticated).Returns(true);
        var snapshots = new Mock<IAccessSnapshotStore>();
        snapshots.Setup(s => s.ReadAsync(It.IsAny<ActorIdentity>(), It.IsAny<AuthorizationTarget>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new AccessEvaluationInput(new(id, user.TenantId, user.DepartmentId, true, user.AuthorizationVersion, new HashSet<string>(), new HashSet<string> { "file.read", "file.upload" }), new(session.Id, id, true, new(session.ExpiresAt), 1, true), null, [], [new("dms:scope:global", 0, false, true)], true, DateTimeOffset.UtcNow, "fixture", new("none", null))));
        Snapshots = snapshots.Object;
        Resolver = new FileAccessContextResolver(current.Object, db, Snapshots);
        Access = new FileAccessService(Snapshots, new FileAclQuery(db), new AccessPolicyEvaluator());
    }
}
