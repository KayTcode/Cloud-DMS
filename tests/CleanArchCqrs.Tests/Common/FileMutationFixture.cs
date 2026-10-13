using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Tests.Common;
public sealed class FileMutationFixture
{
    public AppDbContext Db { get; }
    public User User { get; }
    public RefreshToken Session { get; }
    public FileMutationService Service { get; }
    public Func<Task>? BeforePersist;
    public bool SessionBlocked;
    public string Generation = "g";
    public HashSet<string> Roles = new() { "TenantAdmin" };
    public FileMutationFixture(AppDbContext db, User? user = null)
    {
        Db = db; User = user ?? new User(Guid.NewGuid(), null, "acl@example.test", "hash", "Acl", "Test", null); Session = new(Guid.NewGuid(), User.Id, "test-hash", DateTime.UtcNow.AddHours(1), null);
        if (user is null) { db.Tenants.Add(new() { Id = User.TenantId!.Value, Name = "Test", Code = Guid.NewGuid().ToString("N") }); db.Users.Add(User); }
        db.RefreshTokens.Add(Session); db.SaveChanges();
        var resolver = new Mock<IFileAccessContextResolver>(); resolver.Setup(r => r.ResolveContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FileAccessContext(new(User.Id, Session.Id), User.TenantId!.Value));
        resolver.Setup(r => r.ResolveContextForMutationReceiptAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FileAccessContext(new(User.Id, Session.Id), User.TenantId!.Value));
        var snapshots = new Mock<IAccessSnapshotStore>(); snapshots.Setup(s => s.ReadAsync(It.IsAny<ActorIdentity>(), It.IsAny<AuthorizationTarget>(), It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(new AccessEvaluationInput(new(User.Id, User.TenantId, null, true, User.AuthorizationVersion, Roles, new HashSet<string> { "file.read", "file.delete", "file.share", "department.manage", "user.manage" }), new(Session.Id, User.Id, !SessionBlocked, new DateTimeOffset(Session.ExpiresAt), 1, true), null, Array.Empty<ShareGrant>(), new[] { new ScopeAccessSnapshot("dms:scope:global", db.AuthorizationScopes.AsNoTracking().SingleOrDefault(s => s.ScopeKey == "global")?.Version ?? 0, false, true) }, true, DateTimeOffset.UtcNow, Generation, new("none", null))));
        var coordinator = new Mock<IAuthorizationMutationCoordinator>(); coordinator.Setup(c => c.ExecuteAsync(It.IsAny<MutationRequest>(), It.IsAny<Guid>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>())).Returns(async (MutationRequest request, Guid actor, Func<CancellationToken, Task> persist, CancellationToken ct) =>
        {
            if (BeforePersist is not null) await BeforePersist(); await persist(ct); var scope = db.AuthorizationScopes.SingleOrDefault(s => s.ScopeKey == "global"); if (scope is null) { scope = new() { ScopeKey = "global" }; db.AuthorizationScopes.Add(scope); }
            scope.Version++;
            var operation = new AuthorizationOperation { InitiatedByUserId = actor, ScopeKey = request.ScopeKey, IdempotencyKey = request.IdempotencyKey, PayloadHash = request.PayloadHash, Version = scope.Version, Status = AuthorizationOperationStatus.Applied }; db.AuthorizationOperations.Add(operation); await db.SaveChangesAsync(ct); return new MutationReceipt(operation.Id, operation.Version, operation.Status);
        });
        var acl = new FileAclQuery(db); var evaluator = new AccessPolicyEvaluator(); Service = new(db, resolver.Object, snapshots.Object, new FileAccessService(snapshots.Object, acl, evaluator), acl, evaluator, coordinator.Object);
    }
}
