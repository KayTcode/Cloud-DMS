using System.Reflection;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Search;
using CleanArchCqrs.Infrastructure.Search.Extraction;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Services;
using CleanArchCqrs.Infrastructure.Services.Storage;
using CleanArchCqrs.Infrastructure.Workers;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace CloudDms.IntegrationTests.Files;
[Collection("Search services")]
public sealed class DocumentLeaseIntegrationTests(MySqlFixture mysql,RedisFixture redis,SearchServicesFixture services)
{
    private sealed class Storage(string config) : IStorageServiceFactory
    {
        public IStorageService CreateService(StorageProvider provider)=>new S3StorageService(config);
        public IStorageService CreateServiceFromRawConfig(StorageProviderType type,string json)=>new S3StorageService(json);
    }
    [Fact]
    public async Task BusyFileDoesNotConsumeLeaseOrStarveOtherPendingFileAndCoverageStaysUnreadable()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));var ct=timeout.Token;
        await using var db=mysql.CreateDbContext();var tenant=new Tenant(Guid.NewGuid(),"lease",Guid.NewGuid().ToString("N"),10000);
        var user=new User(tenant.Id,null,$"{Guid.NewGuid():N}@example.test","hash","Lease","Owner",null);
        var provider=new StorageProvider { Name="lease S3",ConfigEncrypted="fixture",TotalCapacityBytes=10000 };
        db.Tenants.Add(tenant);db.Users.Add(user);db.StorageProviders.Add(provider);
        var files=Enumerable.Range(0,3).Select(i=>new FileEntry { TenantId=tenant.Id,OwnerId=user.Id,Name="pending.txt",StorageKey="objects/"+Guid.NewGuid().ToString("N"),StorageProviderId=provider.Id,SizeBytes=100,ContentType="application/octet-stream",ScanStatus="Pending" }).ToArray();
        foreach(var file in files){db.FileEntries.Add(file);db.FileContentVersions.Add(new(){TenantId=tenant.Id,FileId=file.Id,ProviderId=provider.Id,ObjectKey=file.StorageKey,SizeBytes=100,ContentType="application/octet-stream",Hash="fixture",ScanStatus=FileScanStatus.Pending});}
        var now=DateTimeOffset.UtcNow;
        var events=files.Select((f,i)=>new DocumentOutboxMessage {TenantId=tenant.Id,FileId=f.Id,DocumentVersion=1,EventType="DocumentCreated",IdempotencyKey=Guid.NewGuid().ToString("N"),Payload="{}",NextAttemptAt=now.AddMinutes(-3+i)}).ToArray();
        events[2].Status=DocumentOutboxStatus.Processing;events[2].AttemptCount=8;events[2].LeaseOwnerId=Guid.NewGuid();events[2].LeaseExpiresAt=now.AddMinutes(-1);
        db.DocumentOutboxMessages.AddRange(events);await db.SaveChangesAsync(ct);
        using var client=redis.CreateClient();var readiness=new RedisReadiness(client);await new SecurityProjectionBootstrapper(db,client,readiness).RebuildAsync(ct);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ClamAV:Enabled"]="false" }).Build();
        var collection=new ServiceCollection();
        collection.AddScoped(_=>mysql.CreateDbContext());collection.AddScoped<IApplicationDbContext>(s=>s.GetRequiredService<AppDbContext>());
        collection.AddSingleton(client);collection.AddSingleton(readiness);collection.AddScoped<IAuthorizationMutationCoordinator>(s=>new AuthorizationMutationCoordinator(s.GetRequiredService<AppDbContext>(),client,readiness,NullLogger<AuthorizationMutationCoordinator>.Instance));
        collection.AddSingleton<IStorageServiceFactory>(new Storage(services.S3Config));collection.AddSingleton<IVirusScannerService>(new ClamAvVirusScannerService(config,new Microsoft.Extensions.Hosting.Internal.HostingEnvironment(),NullLogger<ClamAvVirusScannerService>.Instance));
        collection.AddSingleton(Options.Create(new DocumentIndexOptions { ClamAvCoverageConfirmed=false }));collection.AddSingleton(Options.Create(new EmbeddingOptions {Endpoint=services.EmbeddingEndpoint}));
        collection.AddSingleton(Options.Create(new QdrantOptions {Endpoint=services.VectorEndpoint,ApiKey=services.VectorKey}));
        collection.AddScoped<IFileTextExtractor,FileTextExtractor>();collection.AddScoped<ITextChunker>(s=>new HttpTextChunker(new HttpClient(),s.GetRequiredService<IOptions<DocumentIndexOptions>>()));
        collection.AddScoped<IEmbeddingService>(s=>new InternalEmbeddingService(new HttpClient(),s.GetRequiredService<IOptions<EmbeddingOptions>>()));
        collection.AddScoped(s=>new QdrantVectorSearchService(new HttpClient { BaseAddress=new Uri(services.VectorEndpoint) },s.GetRequiredService<IOptions<QdrantOptions>>(),s.GetRequiredService<IOptions<EmbeddingOptions>>()));
        collection.AddScoped<IDocumentIndexSink,QdrantDocumentIndexSink>();collection.AddScoped<UploadQuota>();collection.AddScoped<DocumentIndexProcessor>();
        await using var sp=collection.BuildServiceProvider();
        using var worker=new DocumentIndexWorker(sp.GetRequiredService<IServiceScopeFactory>(),Options.Create(new DocumentIndexOptions()),NullLogger<DocumentIndexWorker>.Instance);
        await using var lockDb=mysql.CreateDbContext();await lockDb.Database.OpenConnectionAsync(ct);await using var command=lockDb.Database.GetDbConnection().CreateCommand();
        command.CommandText="SELECT GET_LOCK(@name,0)";var parameter=command.CreateParameter();parameter.ParameterName="@name";parameter.Value=$"dms-index:{files[0].Id:N}";command.Parameters.Add(parameter);Assert.Equal(1,Convert.ToInt32(await command.ExecuteScalarAsync(ct)));
        var run=typeof(DocumentIndexWorker).GetMethod("RunOneAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Assert.True(await (Task<bool>)run.Invoke(worker,new object[]{ct})!);
        Assert.True(await (Task<bool>)run.Invoke(worker,new object[]{ct})!);
        db.ChangeTracker.Clear();
        var busy=await db.DocumentOutboxMessages.SingleAsync(m=>m.Id==events[0].Id,ct);Assert.Equal(0,busy.AttemptCount);Assert.Null(busy.LeaseOwnerId);Assert.True(busy.NextAttemptAt>now);
        var pending=await db.DocumentOutboxMessages.SingleAsync(m=>m.Id==events[1].Id,ct);Assert.Equal(DocumentOutboxStatus.Failed,pending.Status);Assert.Equal("SCAN_AWAITING_VERIFICATION",pending.FailureCode);Assert.Equal(1,pending.AttemptCount);
        Assert.Equal(DocumentOutboxStatus.Failed,(await db.DocumentOutboxMessages.SingleAsync(m=>m.Id==events[2].Id,ct)).Status);
        var current=await db.FileEntries.SingleAsync(f=>f.Id==files[1].Id,ct);Assert.Equal("Pending",current.ScanStatus);Assert.Null(current.ActiveSearchGenerationId);
        Assert.Empty(await (await new FileAclQuery(db).ReadScopeAsync(new(new(user.Id,Guid.NewGuid()),tenant.Id),DateTimeOffset.UtcNow,ct)).ToArrayAsync(ct));
        using var scope=sp.CreateScope();var processor=scope.ServiceProvider.GetRequiredService<DocumentIndexProcessor>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>processor.ProcessAsync(pending,Guid.NewGuid(),ct));
        // Complete the real projection operation before another test rebuilds security state.
        foreach(var operation in await db.AuthorizationOperations.Where(o=>o.Status==AuthorizationOperationStatus.Pending).ToListAsync(ct))
            await new AuthorizationProjectionWriter(db,client).PublishAsync(operation.Id,ct);
    }
}
