using CleanArchCqrs.Tests.Common;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Tests.Security;
public class FileAclQueryTests
{
    [Theory]
    [InlineData(PrincipalKind.User, ResourceKind.File)]
    [InlineData(PrincipalKind.Team, ResourceKind.File)]
    [InlineData(PrincipalKind.User, ResourceKind.Folder)]
    [InlineData(PrincipalKind.Team, ResourceKind.Folder)]
    public async Task CurrentTeamAndAncestorGrantMatchesSearchScope(PrincipalKind principal, ResourceKind resource)
    {
        using var db = TestDbContextFactory.Create(); var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var team = new Team { TenantId = tenant }; var root = new Folder { TenantId = tenant }; var child = new Folder { TenantId = tenant, ParentFolderId = root.Id }; var file = new FileEntry { TenantId = tenant, OwnerId = Guid.NewGuid(), FolderId = child.Id };
        db.Teams.Add(team); db.TeamMembers.Add(new() { TenantId = tenant, TeamId = team.Id, UserId = actor }); db.Folders.AddRange(root, child); db.FileEntries.Add(file);
        db.ResourceGrants.Add(new() { TenantId = tenant, PrincipalKind = principal, PrincipalId = principal == PrincipalKind.User ? actor : team.Id, ResourceKind = resource, ResourceId = resource == ResourceKind.File ? file.Id : root.Id, Role = ResourceRole.Owner });
        db.FileContentVersions.Add(new() { TenantId = tenant, FileId = file.Id, DocumentVersion = 1, ScanStatus = FileScanStatus.Clean }); await db.SaveChangesAsync();
        var context = new FileAccessContext(new(actor, Guid.NewGuid()), tenant); var query = new FileAclQuery(db); var rows = await query.ResolveManyAsync(context, new[] { file.Id }, DateTimeOffset.UtcNow, default);
        Assert.True(rows[file.Id].VerifiedClean); Assert.Contains(rows[file.Id].Grants, g => g.CanRead && g.CanWrite && g.CanShare && g.CanDelete);
        Assert.Equal(new[] { file.Id }, await (await query.ReadScopeAsync(context, DateTimeOffset.UtcNow, default)).Select(f => f.Id).ToArrayAsync());
        if (principal == PrincipalKind.Team) { db.TeamMembers.RemoveRange(db.TeamMembers); await db.SaveChangesAsync(); Assert.Empty((await query.ResolveManyAsync(context, new[] { file.Id }, DateTimeOffset.UtcNow, default))[file.Id].Grants); Assert.Empty(await (await query.ReadScopeAsync(context, DateTimeOffset.UtcNow, default)).ToArrayAsync()); }
    }
    [Fact]
    public async Task FolderCycleAndForeignAncestorFailClosedEvenForOwner()
    {
        using var db = TestDbContextFactory.Create(); var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var a = new Folder { TenantId = tenant }; var b = new Folder { TenantId = tenant, ParentFolderId = a.Id }; a.ParentFolderId = b.Id; var foreign = new Folder { TenantId = Guid.NewGuid() }; var broken = new Folder { TenantId = tenant, ParentFolderId = foreign.Id }; db.Folders.AddRange(a, b, foreign, broken);
        var files = new[] { new FileEntry { TenantId = tenant, OwnerId = actor, FolderId = a.Id }, new FileEntry { TenantId = tenant, OwnerId = actor, FolderId = broken.Id }, new FileEntry { TenantId = foreign.TenantId, OwnerId = actor } }; db.FileEntries.AddRange(files); await db.SaveChangesAsync(); var context = new FileAccessContext(new(actor, Guid.NewGuid()), tenant); var query = new FileAclQuery(db);
        Assert.Empty(await query.ResolveManyAsync(context, files.Select(f => f.Id).ToArray(), DateTimeOffset.UtcNow, default));
        Assert.Empty(await (await query.ReadScopeAsync(context, DateTimeOffset.UtcNow, default)).ToArrayAsync());
    }
}
