using CleanArchCqrs.Tests.Common;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Features.Files.Commands.ManageAccess;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Tests.Security;
public class FileMutationTests
{
    [Fact]
    public async Task ReplayAfterDeleteAndPurgeReturnsSameReceiptAndPayloadConflict()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var file = new FileEntry { TenantId = f.User.TenantId!.Value, OwnerId = f.User.Id }; db.FileEntries.Add(file); await db.SaveChangesAsync();
        var first = await f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default);
        Assert.Equal(FileLifecycle.Binned, file.Lifecycle);
        Assert.Equal(first, await f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default));
        var purge = await f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, "purge", default);
        Assert.Equal(purge, await f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Purging, "purge", default));
        Assert.Equal(2, file.AuthorizationVersion - 1); Assert.Equal(2, await db.DocumentOutboxMessages.CountAsync());
        await Assert.ThrowsAsync<AuthorizationMutationConflictException>(() => f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Active, "purge", default));
    }
    [Fact]
    public async Task TenantAdminWithoutOwnerCannotDeleteContent()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var file = new FileEntry { TenantId = f.User.TenantId!.Value, OwnerId = Guid.NewGuid() }; db.FileEntries.Add(file); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default)); Assert.False(file.IsDeleted);
    }
    [Fact]
    public async Task FolderMoveRechecksConcurrentAncestryInsideCoordinator()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var a = new Folder { TenantId = f.User.TenantId!.Value }; var b = new Folder { TenantId = a.TenantId }; db.Folders.AddRange(a, b); db.ResourceGrants.AddRange(new ResourceGrant { TenantId = a.TenantId, ResourceKind = ResourceKind.Folder, ResourceId = a.Id, PrincipalKind = PrincipalKind.User, PrincipalId = f.User.Id, Role = ResourceRole.Owner }, new ResourceGrant { TenantId = b.TenantId, ResourceKind = ResourceKind.Folder, ResourceId = b.Id, PrincipalKind = PrincipalKind.User, PrincipalId = f.User.Id, Role = ResourceRole.Owner }); await db.SaveChangesAsync();
        f.BeforePersist = async () => { b.ParentFolderId = a.Id; await db.SaveChangesAsync(); };
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => f.Service.ManageAsync(new(AccessMutationKind.MoveFolder, a.Id, Guid.Empty, PrincipalKind.User, ResourceRole.Viewer, null, b.Id, "move"), default)); Assert.Null(a.ParentFolderId);
    }
    [Fact]
    public async Task StaleGlobalScopeInsideCoordinatorRejectsMutation()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var file = new FileEntry { TenantId = f.User.TenantId!.Value, OwnerId = f.User.Id }; db.FileEntries.Add(file); await db.SaveChangesAsync(); f.BeforePersist = async () => { db.AuthorizationScopes.Add(new() { ScopeKey = "global", Version = 99 }); await db.SaveChangesAsync(); };
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default)); Assert.False(file.IsDeleted);
    }
    [Fact]
    public async Task FileOwnerGrantAndTeamMembershipMutationsIncrementVersions()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var tenant = f.User.TenantId!.Value; var file = new FileEntry { TenantId = tenant, OwnerId = f.User.Id }; var team = new Team { TenantId = tenant }; db.FileEntries.Add(file); db.Teams.Add(team); await db.SaveChangesAsync();
        await f.Service.ManageAsync(new(AccessMutationKind.AddTeamMember, team.Id, f.User.Id, PrincipalKind.User, ResourceRole.Viewer, null, null, "member-add"), default);
        Assert.Single(await db.TeamMembers.ToArrayAsync()); Assert.Equal(2, team.AuthorizationVersion);
        await f.Service.ManageAsync(new(AccessMutationKind.UpsertFileGrant, file.Id, team.Id, PrincipalKind.Team, ResourceRole.Editor, DateTimeOffset.UtcNow.AddHours(1), null, "grant-add"), default);
        Assert.Equal(ResourceRole.Editor, (await db.ResourceGrants.SingleAsync()).Role); Assert.Equal(2, file.AuthorizationVersion);
        await f.Service.ManageAsync(new(AccessMutationKind.RevokeFileGrant, file.Id, team.Id, PrincipalKind.Team, ResourceRole.Viewer, null, null, "grant-revoke"), default); Assert.Empty(await db.ResourceGrants.ToArrayAsync()); Assert.Equal(3, file.AuthorizationVersion);
        await f.Service.ManageAsync(new(AccessMutationKind.RemoveTeamMember, team.Id, f.User.Id, PrincipalKind.User, ResourceRole.Viewer, null, null, "member-remove"), default); Assert.Empty(await db.TeamMembers.ToArrayAsync()); Assert.Equal(3, team.AuthorizationVersion);
    }
    [Fact]
    public async Task StructureAdminCannotMoveOrGrantUnownedFolder()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var folder = new Folder { TenantId = f.User.TenantId!.Value }; db.Folders.Add(folder); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => f.Service.ManageAsync(new(AccessMutationKind.MoveFolder, folder.Id, Guid.Empty, PrincipalKind.User, ResourceRole.Viewer, null, null, "move"), default));
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => f.Service.ManageAsync(new(AccessMutationKind.UpsertFolderGrant, folder.Id, f.User.Id, PrincipalKind.User, ResourceRole.Owner, null, null, "grant"), default));
    }
    [Fact]
    public async Task NonAdminOwnerCanManageFolderAclAndMoveWithinOwnedDestination()
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); f.Roles.Clear(); var source = new Folder { TenantId = f.User.TenantId!.Value }; var destination = new Folder { TenantId = source.TenantId }; db.Folders.AddRange(source, destination);
        foreach (var folder in new[] { source, destination }) db.ResourceGrants.Add(new() { TenantId = source.TenantId, ResourceKind = ResourceKind.Folder, ResourceId = folder.Id, PrincipalKind = PrincipalKind.User, PrincipalId = f.User.Id, Role = ResourceRole.Owner }); await db.SaveChangesAsync();
        await f.Service.ManageAsync(new(AccessMutationKind.MoveFolder, source.Id, Guid.Empty, PrincipalKind.User, ResourceRole.Viewer, null, destination.Id, "move"), default); Assert.Equal(destination.Id, source.ParentFolderId);
        await f.Service.ManageAsync(new(AccessMutationKind.UpsertFolderGrant, source.Id, f.User.Id, PrincipalKind.User, ResourceRole.Owner, null, null, "grant"), default); Assert.Equal(3, source.AuthorizationVersion);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SerializedCallbackRejectsRedisBlockAndChangedGeneration(bool block)
    {
        using var db = TestDbContextFactory.Create(); var f = new FileMutationFixture(db); var file = new FileEntry { TenantId = f.User.TenantId!.Value, OwnerId = f.User.Id }; db.FileEntries.Add(file); await db.SaveChangesAsync();
        f.BeforePersist = () => { if (block) f.SessionBlocked = true; else f.Generation = "changed"; return Task.CompletedTask; };
        await Assert.ThrowsAsync<AuthorizationChangedException>(() => f.Service.ChangeLifecycleAsync(file.Id, FileLifecycle.Binned, "delete", default)); Assert.False(file.IsDeleted);
    }
    [Fact]
    public async Task AppliedGrantReplayReturnsOwnedReceiptAfterGrantExpiry()
    {
        using var db=TestDbContextFactory.Create();
        var fixture=new FileMutationFixture(db);
        var file=new FileEntry{TenantId=fixture.User.TenantId!.Value,OwnerId=fixture.User.Id};
        db.FileEntries.Add(file);
        await db.SaveChangesAsync();
        var command=new ManageAccessCommand(AccessMutationKind.UpsertFileGrant,file.Id,fixture.User.Id,
            PrincipalKind.User,ResourceRole.Viewer,DateTimeOffset.UtcNow.AddSeconds(1),null,"grant");
        var receipt=await fixture.Service.ManageAsync(command,default);
        await Task.Delay(1100);
        Assert.Equal(receipt,await fixture.Service.ManageAsync(command,default));
        Assert.Equal(2,file.AuthorizationVersion);
    }}
