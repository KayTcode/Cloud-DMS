using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using CleanArchCqrs.Application.Features.Files.Queries.GetFileText;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Preview;
using CleanArchCqrs.Infrastructure.Search;
using CleanArchCqrs.Infrastructure.Search.Extraction;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Services.Storage;
using CloudDms.IntegrationTests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace CloudDms.IntegrationTests.Files;
[Collection("Search services")]
public sealed class SearchReadEndToEndTests(MySqlFixture mysql, RedisFixture redis, SearchServicesFixture services)
{
    private sealed record Current(Guid? UserId, Guid? SessionId, Guid? TenantId) : ICurrentUserService
    { public Guid? DepartmentId => null; public string? Email => null; public string? Role => "Employee"; public bool IsAuthenticated => true; }
    public sealed record Document(string Id, string Name, string Text, bool MetadataOnly);
    public sealed record Query(string Id, string QueryText, string[] Relevant, string Category);
    private static string CorpusPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null) { var path = Path.Combine(dir.FullName,"tests","fixtures","search-read","corpus.json"); if(File.Exists(path)) return path; dir=dir.Parent; }
        throw new FileNotFoundException("Non-sensitive benchmark corpus is missing.");
    }
    [Fact]
    public async Task ActualE5QdrantMySqlRedisS3SearchReadAndMeasuredCorpus()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4)); var ct=deadline.Token;
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(CorpusPath(),ct));
        var documents = json.RootElement.GetProperty("documents").Deserialize<Document[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
        var queries = json.RootElement.GetProperty("queries").EnumerateArray().Select(q => new Query(q.GetProperty("id").GetString()!,q.GetProperty("query").GetString()!,q.GetProperty("relevant").EnumerateArray().Select(x=>x.GetString()!).ToArray(),q.GetProperty("category").GetString()!)).ToArray();
        Assert.Equal(50,queries.Length);
        await using var db=mysql.CreateDbContext();
        var tenant=new Tenant(Guid.NewGuid(),"search corpus",Guid.NewGuid().ToString("N"),100_000_000);
        var user=new User(tenant.Id,null,$"{Guid.NewGuid():N}@example.test","hash","Corpus","Reader",null);
        var session=new RefreshToken(Guid.NewGuid(),user.Id,Guid.NewGuid().ToString("N"),DateTime.UtcNow.AddHours(1),null);
        var provider=new StorageProvider { Name="isolated S3", ConfigEncrypted="fixture",TotalCapacityBytes=100_000_000 };
        tenant.StorageProviderId=provider.Id;
        db.Tenants.Add(tenant);db.Users.Add(user);db.RefreshTokens.Add(session);db.StorageProviders.Add(provider);
        var role=new Role { Name="ReadFixture-"+Guid.NewGuid().ToString("N") };db.Roles.Add(role);db.UserRoles.Add(new UserRole(Guid.NewGuid(),user.Id,role.Id));
        var permission=await db.Permissions.SingleOrDefaultAsync(p=>p.Name=="file.read",ct);
        if(permission is null){permission=new(Guid.NewGuid(),"file.read",null);db.Permissions.Add(permission);}
        db.RolePermissions.Add(new(Guid.NewGuid(),role.Id,permission.Id));await db.SaveChangesAsync(ct);
        using var storage=new S3StorageService(services.S3Config);
        var embeddingOptions=Options.Create(new EmbeddingOptions { Endpoint=services.EmbeddingEndpoint });
        using var embeddings=new InternalEmbeddingService(new HttpClient(),embeddingOptions);
        var vectorClient=new HttpClient { BaseAddress=new Uri(services.VectorEndpoint) };vectorClient.DefaultRequestHeaders.Add("api-key",services.VectorKey);
        using var vectors=new QdrantVectorSearchService(vectorClient,Options.Create(new QdrantOptions { Endpoint=services.VectorEndpoint,ApiKey=services.VectorKey }),embeddingOptions);
        using var chunker=new HttpTextChunker(new HttpClient(),Options.Create(new DocumentIndexOptions { TokenizerEndpoint=services.EmbeddingEndpoint }));
        var extractor=new FileTextExtractor(Options.Create(new DocumentIndexOptions()));
        var sink=new QdrantDocumentIndexSink(db,embeddings,vectors,embeddingOptions);
        var ids=new Dictionary<string,Guid>();var generations=new Dictionary<string,Guid>();
        var indexTimer=Stopwatch.StartNew();
        foreach(var document in documents)
        {
            var bytes=Encoding.UTF8.GetBytes(document.Text);using var upload=new MemoryStream(bytes);
            var key=await storage.UploadFileAsync(upload,document.Name,"application/octet-stream",cancellationToken:ct);
            var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var file=new FileEntry { TenantId=tenant.Id,OwnerId=user.Id,Name=document.Name,StorageKey=key,StorageProviderId=provider.Id,ContentType=document.MetadataOnly?"application/octet-stream":"text/plain",SizeBytes=bytes.Length,Hash=hash,ScanStatus="Clean" };
            ids[document.Id]=file.Id;db.FileEntries.Add(file);
            // Trusted synthetic fixtures seed scan status. This does not validate a real AV daemon.
            db.FileContentVersions.Add(new() { TenantId=tenant.Id,FileId=file.Id,ProviderId=provider.Id,ObjectKey=key,ContentType=file.ContentType,SizeBytes=bytes.Length,Hash=hash,ScanStatus=FileScanStatus.Clean });
            await db.SaveChangesAsync(ct);
            if(document.MetadataOnly)continue;
            using var readStorage=new S3StorageService(services.S3Config);await using var downloaded=await readStorage.DownloadFileAsync(key,ct);using var local=new MemoryStream();await downloaded.CopyToAsync(local,ct);local.Position=0;
            Assert.Equal(hash,Convert.ToHexString(SHA256.HashData(local.ToArray())).ToLowerInvariant());
            var extracted=await extractor.ExtractAsync(local,document.Name,ct);
            var chunked=await chunker.ChunkAsync(extracted.Sections,ct);Assert.False(chunked.IsPartial);
            var generation=new FileSearchGeneration { TenantId=tenant.Id,FileId=file.Id,DocumentVersion=1,ModelVersion=SearchPolicy.Model,ExtractorVersion="fixture-extractor",ChunkerVersion=chunked.ChunkerVersion,Status=FileSearchStatus.Pending };
            db.FileSearchGenerations.Add(generation);await db.SaveChangesAsync(ct);
            foreach(var chunk in chunked.Chunks)db.FileTextChunks.Add(new() { TenantId=tenant.Id,GenerationId=generation.Id,Ordinal=chunk.Ordinal,Text=chunk.Text,TokenCount=chunk.TokenCount,Location=chunk.Location });
            await db.SaveChangesAsync(ct);await sink.StageAsync(generation.Id,ct);await sink.StageAsync(generation.Id,ct); // actual idempotent upsert
            generation.Status=FileSearchStatus.Ready;generation.PublishedAt=DateTimeOffset.UtcNow;file.ActiveSearchGenerationId=generation.Id;generations[document.Id]=generation.Id;await db.SaveChangesAsync(ct);
        }
        indexTimer.Stop();
        using var client=redis.CreateClient();var ready=new RedisReadiness(client);await new SecurityProjectionBootstrapper(db,client,ready).RebuildAsync(ct);
        var snapshots=new RedisAccessSnapshotStore(client,ready);var current=new Current(user.Id,session.Id,tenant.Id);
        var resolver=new FileAccessContextResolver(current,db,snapshots);var acl=new FileAclQuery(db);var access=new FileAccessService(snapshots,acl,new AccessPolicyEvaluator());
        var protection=new EphemeralDataProtectionProvider();var store=new SearchSessionStore(client,protection);
        var handler=new SearchFilesQueryHandler(resolver,access,new MySqlKeywordSearchService(db,acl),embeddings,vectors,store,new SearchCandidateReader(db,acl));
        var measurements=new List<object>();
        foreach(var mode in new[]{"keyword","semantic","hybrid"})
        {
            var samples=new List<double>();double recall=0,ndcg=0;
            foreach(var query in queries)
            {
                var watch=Stopwatch.StartNew();var result=await handler.Handle(new(query.QueryText,mode,PageSize:10),ct);watch.Stop();
                Assert.False(result.Degraded);Assert.Equal(mode,result.ModeUsed);
                var relevant=query.Relevant.Select(id=>ids[id]).ToHashSet();
                recall+=(double)result.Items.Take(5).Count(i=>relevant.Contains(i.FileId))/relevant.Count;
                var dcg=result.Items.Take(10).Select((item,index)=>relevant.Contains(item.FileId)?1/Math.Log2(index+2):0).Sum();
                var ideal=Enumerable.Range(0,Math.Min(10,relevant.Count)).Sum(index=>1/Math.Log2(index+2));ndcg+=dcg/ideal;samples.Add(watch.Elapsed.TotalMilliseconds);
                Assert.All(result.Items,item=>Assert.Contains(item.FileId,ids.Values));
            }
            measurements.Add(new { mode,queries=queries.Length,recallAt5=recall/queries.Length,ndcgAt10=ndcg/queries.Length,p95Milliseconds=samples.Order().ElementAt((int)Math.Ceiling(samples.Count*.95)-1) });
        }
        var first=await handler.Handle(new("tài liệu","semantic",PageSize:1),ct);Assert.NotNull(first.NextCursor);
        var cursorQuery=new SearchFilesQuery("tài liệu","semantic",PageSize:1,Cursor:first.NextCursor);
        var page=await handler.Handle(cursorQuery,ct);var replay=await handler.Handle(cursorQuery,ct);
        Assert.Equal(page.Items.Select(i=>i.FileId),replay.Items.Select(i=>i.FileId));Assert.DoesNotContain(page.Items,i=>i.FileId==first.Items[0].FileId);
        await Assert.ThrowsAsync<InvalidSearchRequestException>(()=>handler.Handle(cursorQuery with { Query="different" },ct));
        await Assert.ThrowsAsync<InvalidSearchRequestException>(()=>handler.Handle(cursorQuery with { Cursor=first.NextCursor+"tampered" },ct));
        var decoded=store.Unprotect(first.NextCursor!);
        await Assert.ThrowsAsync<InvalidSearchRequestException>(()=>handler.Handle(cursorQuery with { Cursor=store.Protect(decoded with { UserId=Guid.NewGuid() }) },ct));
        await Assert.ThrowsAsync<InvalidSearchRequestException>(()=>handler.Handle(cursorQuery with { Cursor=store.Protect(decoded with { ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1) }) },ct));
        var textProtector=new FileTextCursorProtector(protection);
        var text=new GetFileTextQueryHandler(resolver,access,db,textProtector,new FileTextOptions(SearchPolicy.Model));
        for(var ordinal=1;ordinal<105;ordinal++)db.FileTextChunks.Add(new(){TenantId=tenant.Id,GenerationId=generations["budget"],Ordinal=ordinal,Text=$"Synthetic pagination section {ordinal}",TokenCount=10});
        await db.SaveChangesAsync(ct);
        var textPage=await text.Handle(new(ids["budget"],20),ct);Assert.Equal("ready",textPage.Status);Assert.Equal(20,textPage.Items.Count);Assert.NotNull(textPage.NextCursor);
        var textNext=await text.Handle(new(ids["budget"],20,textPage.NextCursor),ct);Assert.Equal(20,textNext.Items[0].Ordinal);
        Assert.Equal(100,(await text.Handle(new(ids["budget"],100),ct)).Items.Count);
        await Assert.ThrowsAsync<InvalidSearchRequestException>(()=>text.Handle(new(ids["budget"],20,textPage.NextCursor+"tampered"),ct));
        var textCursor=textProtector.Unprotect(textPage.NextCursor!);
        await Assert.ThrowsAsync<InvalidSearchRequestException>(()=>text.Handle(new(ids["budget"],20,textProtector.Protect(textCursor with { UserId=Guid.NewGuid() })),ct));
        await Assert.ThrowsAsync<CleanArchCqrs.Application.Common.Exceptions.AuthorizationChangedException>(()=>text.Handle(new(ids["budget"],20,textProtector.Protect(textCursor with { GenerationId=Guid.NewGuid() })),ct));
        await services.PauseEmbeddingAsync();
        try
        {
            var fallback=await handler.Handle(new("ngân sách","hybrid"),ct);Assert.True(fallback.Degraded);Assert.Equal("keyword",fallback.ModeUsed);Assert.Contains("SEMANTIC_UNAVAILABLE",fallback.Warnings);
            await Assert.ThrowsAsync<SearchUnavailableException>(()=>handler.Handle(new("ngân sách","semantic"),ct));
        }
        finally { await services.ResumeEmbeddingAsync(); }
        var oldFile=await db.FileEntries.SingleAsync(f=>f.Id==ids["backup"],ct);oldFile.DocumentVersion=2;oldFile.ActiveSearchGenerationId=null;oldFile.ScanStatus="Pending";await db.SaveChangesAsync(ct);
        var replaced=await handler.Handle(new("backup recovery","hybrid",PageSize:10),ct);Assert.DoesNotContain(replaced.Items,i=>i.FileId==oldFile.Id);
        Assert.Null(await access.CheckAsync(await resolver.ResolveContextAsync(ct),oldFile.Id,PermissionNames.FileRead,ct));
        await client.ExecuteAsync("block-session",new[]{$"auth:session:{session.Id:N}"},Array.Empty<string>(),ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>handler.Handle(new("AI","keyword"),ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>text.Handle(new(ids["budget"]),ct));
        var output=Environment.GetEnvironmentVariable("CLOUD_DMS_BENCHMARK_OUTPUT")??Path.Combine(Path.GetTempPath(),"cloud-dms-task12-benchmark.json");
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new { utc=DateTimeOffset.UtcNow,scope="Service integration; seeded trusted scan/generation publication; no API/AV/worker timing",documents=documents.Length,queries=queries.Length,concurrency=1,embeddingCpuLimit=2,embeddingMemoryMiB=1200,indexDurationMilliseconds=indexTimer.Elapsed.TotalMilliseconds,measurements,resourceSamples=services.ResourceSamples },new JsonSerializerOptions { WriteIndented=true }),ct);
    }
}
