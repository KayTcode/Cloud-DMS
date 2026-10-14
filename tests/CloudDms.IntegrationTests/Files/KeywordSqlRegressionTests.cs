using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Search;
using CleanArchCqrs.Infrastructure.Security;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
namespace CloudDms.IntegrationTests.Files;
[Collection("File ACL")]
public sealed class KeywordSqlRegressionTests(MySqlFixture mysql)
{
    [Fact]
    public async Task LiteralWildcardShortVietnameseAndAclUseActualMySql()
    {
        await using var db = mysql.CreateDbContext();
        var tenant = new Tenant(Guid.NewGuid(), "keyword", Guid.NewGuid().ToString("N"), 1000);
        var other = new Tenant(Guid.NewGuid(), "other", Guid.NewGuid().ToString("N"), 1000);
        var actor = new User(tenant.Id, null, $"{Guid.NewGuid():N}@example.test", "hash", "A", "B", null);
        var stranger = new User(other.Id, null, $"{Guid.NewGuid():N}@example.test", "hash", "A", "B", null);
        var provider = new StorageProvider { Name = "keyword fixture", ConfigEncrypted = "fixture", TotalCapacityBytes = 1000 };
        db.Tenants.AddRange(tenant, other); db.Users.AddRange(actor, stranger); db.StorageProviders.Add(provider);
        var names = new[] { "Kế hoạch ngân sách.txt", "a_b.txt", "a%b.txt", "axb.txt", "AI.txt", "' OR 1=1 --.txt" };
        var files = names.Select(name => new FileEntry { TenantId = tenant.Id, OwnerId = actor.Id, Name = name, ContentType = "text/plain", StorageKey = Guid.NewGuid().ToString("N"), StorageProviderId = provider.Id }).ToArray();
        var foreign = new FileEntry { TenantId = other.Id, OwnerId = stranger.Id, Name = names[0], ContentType = "text/plain", StorageKey = "foreign", StorageProviderId = provider.Id };
        var pending = new FileEntry { TenantId = tenant.Id, OwnerId = actor.Id, Name = names[0], ContentType = "text/plain", StorageKey = "pending", StorageProviderId = provider.Id };
        db.FileEntries.AddRange(files.Append(foreign).Append(pending));
        foreach(var file in files.Append(foreign).Append(pending)) db.FileContentVersions.Add(new() { TenantId = file.TenantId, FileId = file.Id, ProviderId = provider.Id, ObjectKey = file.StorageKey, Hash = "fixture", ContentType = "text/plain", ScanStatus = file == pending ? FileScanStatus.Pending : FileScanStatus.Clean });
        await db.SaveChangesAsync();
        var service = new MySqlKeywordSearchService(db, new FileAclQuery(db));
        var context = new FileAccessContext(new(actor.Id, Guid.NewGuid()), tenant.Id);
        foreach(var (query, expected) in new[] { ("ngân sách",0), ("a_b",1), ("a%b",2), ("AI",4), ("' OR 1=1 --",5) })
        {
            var result = await service.SearchAsync(context, new(query,null,20,SearchPolicy.Model),0,default);
            Assert.Equal(files[expected].Id, Assert.Single(result.Candidates).FileId);
            Assert.False(result.HasMore);
        }
        var none = await service.SearchAsync(context, new("missing",null,20,SearchPolicy.Model),0,default);
        Assert.Empty(none.Candidates);
        var target = files[3];
        var generation = new FileSearchGeneration { TenantId=tenant.Id,FileId=target.Id,
            DocumentVersion=1,ModelVersion=SearchPolicy.Model,ExtractorVersion="fixture",ChunkerVersion="fixture",Status=FileSearchStatus.Ready };
        db.FileSearchGenerations.Add(generation); await db.SaveChangesAsync();
        var texts = new[] { "unrelated section", "galaxy financial forecast", "galaxy galaxy galaxy forecast forecast" };
        foreach(var (text,ordinal) in texts.Select((text,ordinal)=>(text,ordinal)))
            db.FileTextChunks.Add(new() { TenantId=tenant.Id,GenerationId=generation.Id,Ordinal=ordinal,Text=text,TokenCount=10 });
        target.ActiveSearchGenerationId=generation.Id; await db.SaveChangesAsync();
        var expectedChunk = await db.FileTextChunks.Where(c=>c.GenerationId==generation.Id)
            .Select(c=>new { c.Ordinal, Score=EF.Functions.Match(c.Text,"galaxy",MySqlMatchSearchMode.NaturalLanguage) })
            .Where(c=>c.Score>0).OrderByDescending(c=>c.Score).ThenBy(c=>c.Ordinal).FirstAsync();
        var body = await service.SearchAsync(context,new("galaxy",null,20,SearchPolicy.Model),0,default);
        var candidate = Assert.Single(body.Candidates);
        Assert.Equal(target.Id,candidate.FileId); Assert.Equal(expectedChunk.Ordinal,candidate.ChunkOrdinal);
        Assert.Equal(expectedChunk.Score,candidate.Score,8);
    }
}
