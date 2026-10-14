using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Commands.UploadFile;
using CleanArchCqrs.Application.Features.Files.Commands.BeginMultipartUpload;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Services;
using CleanArchCqrs.Infrastructure.Services.Storage;
using CloudDms.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using CleanArchCqrs.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
namespace CloudDms.IntegrationTests.Files;
[Collection("Search services")]
public sealed class ApiRuntimeIntegrationTests(MySqlFixture mysql,RedisFixture redis,SearchServicesFixture services)
{
    private sealed record Current(Guid? UserId,Guid? SessionId,Guid? TenantId):ICurrentUserService
    {public Guid? DepartmentId=>null;public string? Email=>null;public string? Role=>"Employee";public bool IsAuthenticated=>true;}
    private sealed class LostCommitAcknowledgement:DbTransactionInterceptor
    {
        private int remaining=1;
        public override Task TransactionCommittedAsync(DbTransaction transaction,TransactionEndEventData eventData,CancellationToken cancellationToken=default)
        {
            if(Interlocked.Exchange(ref remaining,0)==1)throw new TimeoutException("Synthetic lost commit acknowledgement after real MySQL commit.");
            return Task.CompletedTask;
        }
    }
    private static string Root()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null){if(File.Exists(Path.Combine(dir.FullName,"ARCHITECTURE.md")))return dir.FullName;dir=dir.Parent;}
        throw new DirectoryNotFoundException();
    }
    [Fact]
    public async Task RealProductionHostLoginRefreshMultipartPendingHttpAndSecurityFailure()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var ct=timeout.Token;
        var jwtSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var encryptionSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Encryption:SecretKey"]=encryptionSecret}).Build();
        await using var db=mysql.CreateDbContext();
        var provider=new StorageProvider {Name="API test",Type=StorageProviderType.S3,ConfigEncrypted=new AesEncryptionService(config).Encrypt(services.S3Config),TotalCapacityBytes=1000000,UsedCapacityBytes=20};
        var tenant=new Tenant(Guid.NewGuid(),"API runtime",Guid.NewGuid().ToString("N"),1000000){StorageProviderId=provider.Id,StorageUsedBytes=20};
        var department=new Department {TenantId=tenant.Id,Name="API department",Code="API",QuotaBytes=1000000,UsedBytes=20};db.Departments.Add(department);
        var password=Guid.NewGuid().ToString("N")[..14]+"aA!7";
        var user=new User(tenant.Id,department.Id,$"{Guid.NewGuid():N}@example.test",new PasswordHasher().Hash(password),"API","Reader",null){EmailConfirmed=true};
        var role=new Role{Name="ApiTest-"+Guid.NewGuid().ToString("N")};
        db.StorageProviders.Add(provider);db.Tenants.Add(tenant);db.Users.Add(user);db.Roles.Add(role);db.UserRoles.Add(new(Guid.NewGuid(),user.Id,role.Id));db.UserStorageQuotas.Add(new(){UserId=user.Id,DepartmentId=department.Id,QuotaBytes=1000000,UsedBytes=20});
        foreach(var name in new[]{"file.read","file.upload","file.delete"}){var permission=await db.Permissions.SingleOrDefaultAsync(p=>p.Name==name,ct);if(permission is null){permission=new(Guid.NewGuid(),name,null);db.Permissions.Add(permission);}db.RolePermissions.Add(new(Guid.NewGuid(),role.Id,permission.Id));}
        using var storage=new S3StorageService(services.S3Config);using var fixtureBytes=new MemoryStream("trusted read fixture"u8.ToArray());
        var objectKey=await storage.UploadFileAsync(fixtureBytes,"read.txt","text/plain",cancellationToken:ct);
        var fixtureHash=Convert.ToHexString(SHA256.HashData(fixtureBytes.ToArray())).ToLowerInvariant();
        var clean=new FileEntry{TenantId=tenant.Id,OwnerId=user.Id,Name="read.txt",StorageProviderId=provider.Id,StorageKey=objectKey,ContentType="text/plain",Hash=fixtureHash,SizeBytes=20,ScanStatus="Clean"};
        db.FileEntries.Add(clean);db.FileContentVersions.Add(new(){TenantId=tenant.Id,FileId=clean.Id,ProviderId=provider.Id,ObjectKey=objectKey,ContentType="text/plain",Hash=fixtureHash,SizeBytes=20,ScanStatus=FileScanStatus.Clean});
        await db.SaveChangesAsync(ct);
        using(var client=redis.CreateClient())await new SecurityProjectionBootstrapper(db,client,new RedisReadiness(client)).RebuildAsync(ct);
        var temp=Path.Combine(Path.GetTempPath(),"cloud-dms-api-task12-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        var pfxPassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var rsa=RSA.Create(2048);var certRequest=new CertificateRequest("CN=CloudDms isolated tests",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var cert=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));var certPath=Path.Combine(temp,"test.pfx");await File.WriteAllBytesAsync(certPath,cert.Export(X509ContentType.Pfx,pfxPassword),ct);
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var si=new ProcessStartInfo("dotnet"){WorkingDirectory=temp,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        si.ArgumentList.Add(Path.Combine(Root(),"src","CleanArchCqrs.API","bin","Debug","net8.0","CleanArchCqrs.API.dll"));
        var env=new Dictionary<string,string>{["ASPNETCORE_ENVIRONMENT"]="Production",["DOTNET_ENVIRONMENT"]="Production",["ASPNETCORE_URLS"]=$"http://127.0.0.1:{port}",["ConnectionStrings__DefaultConnection"]=db.Database.GetConnectionString()!,["SecurityRedis__ConnectionString"]=redis.ConnectionString,["JwtSettings__SecretKey"]=jwtSecret,["JwtSettings__Issuer"]="isolated-api-test",["JwtSettings__Audience"]="isolated-client",["Encryption__SecretKey"]=encryptionSecret,["Database__InitializeOnStartup"]="false",["SearchIndex__Enabled"]="false",["ClamAV__Enabled"]="false",["Preview__Enabled"]="false",["Embedding__Endpoint"]=services.EmbeddingEndpoint,["Qdrant__Endpoint"]=services.VectorEndpoint,["Qdrant__ApiKey"]=services.VectorKey,["ReadAccess__PublicApiOrigin"]="https://api.example.test",["DataProtection__ApplicationName"]="isolated-"+Guid.NewGuid().ToString("N"),["DataProtection__KeyRingPath"]=Path.Combine(temp,"keys"),["DataProtection__CertificatePath"]=certPath,["DataProtection__CertificatePassword"]=pfxPassword,["AllowedHosts"]="127.0.0.1",["RateLimiting__SearchPerMinute"]="2",["RateLimiting__AuthPerMinute"]="20",["Logging__LogLevel__Default"]="Error"};
        foreach(var pair in env)si.Environment[pair.Key]=pair.Value;
        using var process=Process.Start(si)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try
        {
            using var http=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}")};
            using var objects=new HttpClient();
            while(true){if(process.HasExited)throw new InvalidOperationException("Isolated API startup failed; no application secrets or logs emitted.");try{using var probe=await http.GetAsync("api/files/search?q=AI",ct);if(probe.StatusCode==HttpStatusCode.Unauthorized)break;}catch(HttpRequestException){}await Task.Delay(100,ct);}
            using(var invalid=await http.PostAsJsonAsync("api/auth/login",new{Email=user.Email,Password=new string('x',21)},ct))
            {Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal("no-store",invalid.Headers.CacheControl!.ToString());}
            using var login=await http.PostAsJsonAsync("api/auth/login",new{Email=user.Email,Password=password},ct);
            if(login.StatusCode!=HttpStatusCode.OK)
            {
                process.Kill(true);await process.WaitForExitAsync(ct);var diagnostic=await stdout;
                var types=System.Text.RegularExpressions.Regex.Matches(diagnostic,@"Unhandled ([A-Za-z0-9_.]+) while").Select(m=>m.Groups[1].Value);
                throw new InvalidOperationException("Isolated API login failed: "+login.StatusCode+"; exception types only: "+string.Join(",",types));
            }
            using var loginJson=JsonDocument.Parse(await login.Content.ReadAsStringAsync(ct));var token=loginJson.RootElement.GetProperty("accessToken").GetString()!;var refresh=loginJson.RootElement.GetProperty("refreshToken").GetString()!;
            http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using(var read=await http.GetAsync($"api/files/{clean.Id:D}/read-access?purpose=download",ct))
            {
                Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.Equal("no-store",read.Headers.CacheControl!.ToString());
                using var readJson=JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
                var readUrl=readJson.RootElement.GetProperty("value").GetProperty("url").GetString();
                using var download=await objects.GetAsync(readUrl,ct);Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal("trusted read fixture",await download.Content.ReadAsStringAsync(ct));
            }
            using(var legacy=await http.GetAsync($"api/files/{clean.Id:D}/download",ct)){Assert.Equal(HttpStatusCode.OK,legacy.StatusCode);Assert.Equal("trusted read fixture",await legacy.Content.ReadAsStringAsync(ct));}
            using(var search=await http.GetAsync("api/files/search?q=AI&mode=keyword",ct)){Assert.Equal(HttpStatusCode.OK,search.StatusCode);Assert.Equal("nosniff",search.Headers.GetValues("X-Content-Type-Options").Single());Assert.Equal("no-store",search.Headers.CacheControl!.ToString());}
            using(var missing=await http.GetAsync($"api/files/{Guid.NewGuid():D}/text",ct)){Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);Assert.Equal("no-store",missing.Headers.CacheControl!.ToString());}
            using(var limited=await http.GetAsync("api/files/search?q=AI&mode=keyword",ct)){Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);Assert.Equal("no-store",limited.Headers.CacheControl!.ToString());}
            using var request=new HttpRequestMessage(HttpMethod.Post,"api/files/upload-sessions"){Content=JsonContent.Create(new{FileName="awaiting.txt",SizeBytes=12})};request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
            using var begin=await http.SendAsync(request,ct);Assert.Equal(HttpStatusCode.OK,begin.StatusCode);using var beginJson=JsonDocument.Parse(await begin.Content.ReadAsStringAsync(ct));var uploadId=beginJson.RootElement.GetProperty("sessionId").GetGuid();var fileId=beginJson.RootElement.GetProperty("fileId").GetGuid();
            using var part=await http.PostAsync($"api/files/upload-sessions/{uploadId:D}/parts/1/url",null,ct);Assert.Equal(HttpStatusCode.OK,part.StatusCode);using var partJson=JsonDocument.Parse(await part.Content.ReadAsStringAsync(ct));
            using var put=await objects.PutAsync(partJson.RootElement.GetProperty("url").GetString(),new ByteArrayContent("hello world!"u8.ToArray()),ct);Assert.Equal(HttpStatusCode.OK,put.StatusCode);
            using var complete=await http.PostAsJsonAsync($"api/files/upload-sessions/{uploadId:D}/complete",new{Parts=new[]{new{Number=1,ETag=put.Headers.ETag!.ToString()}}},ct);Assert.Equal(HttpStatusCode.Accepted,complete.StatusCode);
            db.ChangeTracker.Clear();var file=await db.FileEntries.SingleAsync(f=>f.Id==fileId,ct);Assert.Equal("Pending",file.ScanStatus);Assert.Null(file.ActiveSearchGenerationId);Assert.Equal(32,(await db.Tenants.SingleAsync(t=>t.Id==tenant.Id,ct)).StorageUsedBytes);
            using(var blocked=await http.GetAsync($"api/files/{fileId:D}/download",ct))Assert.False(blocked.IsSuccessStatusCode);
            using var completedJson=JsonDocument.Parse(await complete.Content.ReadAsStringAsync(ct));var operationId=completedJson.RootElement.GetProperty("receipt").GetProperty("operationId").GetGuid();
            while(await db.AuthorizationOperations.AsNoTracking().Where(o=>o.Id==operationId).Select(o=>o.Status).SingleAsync(ct)!=AuthorizationOperationStatus.Applied)await Task.Delay(100,ct);
            using var renewed=await http.PostAsJsonAsync("api/auth/refresh",new{RefreshTokenValue=refresh},ct);Assert.Equal(HttpStatusCode.OK,renewed.StatusCode);
            using var renewedJson=JsonDocument.Parse(await renewed.Content.ReadAsStringAsync(ct));http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",renewedJson.RootElement.GetProperty("value").GetProperty("accessToken").GetString());
            // Inject failure only after an actual MySQL commit, with the production retry strategy enabled.
            var activeSession=await db.RefreshTokens.AsNoTracking().Where(s=>s.UserId==user.Id&&s.RevokedAt==null).Select(s=>s.Id).SingleAsync(ct);
            var options=new DbContextOptionsBuilder<AppDbContext>().UseMySql(db.Database.GetConnectionString(),new MySqlServerVersion(new Version(8,4,0)),o=>o.EnableRetryOnFailure(2,TimeSpan.Zero,null)).AddInterceptors(new LostCommitAcknowledgement()).Options;
            var current=new Current(user.Id,activeSession,tenant.Id);await using var retryDb=new AppDbContext(options,new DateTimeProvider(),current);
            using(var securityClient=redis.CreateClient())
            {
                var readiness=new RedisReadiness(securityClient);var snapshots=new RedisAccessSnapshotStore(securityClient,readiness);var acl=new FileAclQuery(retryDb);var evaluator=new AccessPolicyEvaluator();var access=new FileAccessService(snapshots,acl,evaluator);
                var authorization=new UploadAuthorization(retryDb,new FileAccessContextResolver(current,retryDb,snapshots),snapshots,acl,access,evaluator);
                var multipart=new MultipartUploadService(retryDb,authorization,new UploadQuota(retryDb),new StorageServiceFactory(new AesEncryptionService(config),config),new AuthorizationMutationCoordinator(retryDb,securityClient,readiness,NullLogger<AuthorizationMutationCoordinator>.Instance),config);
                var requestKey=Guid.NewGuid().ToString("N");var reservation=await multipart.BeginAsync(new("retry.txt",5,null,null,requestKey),ct);
                Assert.Equal(reservation.SessionId,(await multipart.BeginAsync(new("retry.txt",5,null,null,requestKey),ct)).SessionId);
                Assert.Equal(37,await retryDb.Tenants.AsNoTracking().Where(t=>t.Id==tenant.Id).Select(t=>t.StorageUsedBytes).SingleAsync(ct));
                Assert.True(await multipart.AbortAsync(new(reservation.SessionId),ct));Assert.True(await multipart.AbortAsync(new(reservation.SessionId),ct));
                Assert.Equal(32,await retryDb.Tenants.AsNoTracking().Where(t=>t.Id==tenant.Id).Select(t=>t.StorageUsedBytes).SingleAsync(ct));
                var expiring=await multipart.BeginAsync(new("expire.txt",6,null,null,Guid.NewGuid().ToString("N")),ct);
                await retryDb.FileUploadSessions.Where(s=>s.Id==expiring.SessionId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExpiresAt,DateTimeOffset.UtcNow.AddHours(-1)),ct);
                var legacyIntent=new LegacyUploadIntent {TenantId=tenant.Id,UserId=user.Id,ProviderId=provider.Id,FileId=Guid.NewGuid(),Status="Abandoned",ExpiresAt=DateTimeOffset.UtcNow.AddHours(-1)};
                retryDb.LegacyUploadIntents.Add(legacyIntent);await retryDb.SaveChangesAsync(ct);
                using var legacyObject=new MemoryStream("orphan fixture"u8.ToArray());var legacyKey=await storage.UploadForIntentAsync(legacyObject,legacyIntent.Id,"text/plain",null,ct);
                var recoveryServices=new ServiceCollection();recoveryServices.AddScoped(_=>new AppDbContext(options,new DateTimeProvider(),current));recoveryServices.AddScoped<IApplicationDbContext>(s=>s.GetRequiredService<AppDbContext>());recoveryServices.AddScoped<UploadQuota>();recoveryServices.AddSingleton<IStorageServiceFactory>(new StorageServiceFactory(new AesEncryptionService(config),config));
                await using var recoveryProvider=recoveryServices.BuildServiceProvider();using var recovery=new MultipartUploadRecoveryWorker(recoveryProvider.GetRequiredService<IServiceScopeFactory>(),NullLogger<MultipartUploadRecoveryWorker>.Instance);
                var recover=typeof(MultipartUploadRecoveryWorker).GetMethod("RecoverAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
                await (Task)recover.Invoke(recovery,new object[]{ct})!;await (Task)recover.Invoke(recovery,new object[]{ct})!;
                Assert.Equal(FileUploadStatus.Expired,await retryDb.FileUploadSessions.AsNoTracking().Where(s=>s.Id==expiring.SessionId).Select(s=>s.Status).SingleAsync(ct));
                Assert.Equal(32,await retryDb.Tenants.AsNoTracking().Where(t=>t.Id==tenant.Id).Select(t=>t.StorageUsedBytes).SingleAsync(ct));
                Assert.NotNull(await retryDb.LegacyUploadIntents.AsNoTracking().Where(i=>i.Id==legacyIntent.Id).Select(i=>i.StorageCleanedAt).SingleAsync(ct));Assert.Null(await storage.InspectAsync(legacyKey,ct));
            }
            // A new authenticated session after rotation still uses the Redis authority.
            await redis.StopAsync();
            try{using var unavailable=await http.GetAsync($"api/files/{clean.Id:D}/read-access?purpose=download",ct);Assert.Equal(HttpStatusCode.ServiceUnavailable,unavailable.StatusCode);Assert.Equal("no-store",unavailable.Headers.CacheControl!.ToString());}
            finally{await redis.StartAsync();}
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await stdout;await stderr;
            var absolute=Path.GetFullPath(temp);if(!absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("cloud-dms-api-task12-"))throw new InvalidOperationException("Unexpected test cleanup target");
            Directory.Delete(absolute,true);
        }
    }
}
