using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CleanArchCqrs.Infrastructure.Services.Storage;
namespace CloudDms.IntegrationTests.Support;
public sealed class SearchServicesFixture : IAsyncLifetime
{
    private readonly List<string> containers = [];
    private readonly CancellationTokenSource monitorStop = new();
    private Task? monitor;
    private readonly List<string> resourceSamples = [];
    public string[] ResourceSamples { get { lock(resourceSamples) return resourceSamples.ToArray(); } }
    private readonly string access = "test" + Guid.NewGuid().ToString("N");
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string EmbeddingEndpoint { get; private set; } = "";
    public string VectorEndpoint { get; private set; } = "";
    public string S3Endpoint { get; private set; } = "";
    public string PreviewEndpoint { get; private set; } = "";
    private string embeddingContainer = "";
    public string VectorKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string S3Config => JsonSerializer.Serialize(new { Endpoint = S3Endpoint, Bucket = "search-test", Region = "us-east-1", AccessKey = access, SecretKey = secret, CapacityBytes = 100_000_000L });
    public Task PauseEmbeddingAsync() => Docker("pause", embeddingContainer);
    public Task ResumeEmbeddingAsync() => Docker("unpause", embeddingContainer);
    public async Task InitializeAsync()
    {
        try
        {
            S3Endpoint = await Start("cloud-dms-minio-tests:2025-10-15", 9000, 512,
                new() { ["MINIO_ROOT_USER"] = access, ["MINIO_ROOT_PASSWORD"] = secret }, "server", "/data");
            VectorEndpoint = await Start("qdrant/qdrant:v1.19.2@sha256:b7b0444c4c351c970b98e90a6f89c2ee4287c65b44e52b4cb503fa5b2aa927ad", 6333, 512,
                new() { ["QDRANT__SERVICE__API_KEY"] = VectorKey });
            EmbeddingEndpoint = await Start("cloud-dms-embedding-task12", 8001, 1200, []);
            embeddingContainer=containers[^1];
            PreviewEndpoint = await Start("cloud-dms-preview-task12", 8002, 512, []);
            using var http = new HttpClient();
            await Ready(http, S3Endpoint + "minio/health/live");
            http.DefaultRequestHeaders.Add("api-key", VectorKey);
            await Ready(http, VectorEndpoint + "readyz");
            http.DefaultRequestHeaders.Remove("api-key");
            await Ready(http, EmbeddingEndpoint + "health");
            using var s3 = new AmazonS3Client(new BasicAWSCredentials(access, secret), new AmazonS3Config { ServiceURL = S3Endpoint.TrimEnd('/'), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = "search-test" });
            monitor=Task.Run(async()=>
            {
                while(!monitorStop.IsCancellationRequested)
                {
                    try
                    {
                        var args=new List<string>{"stats","--no-stream","--format","{{json .}}"};args.AddRange(containers);
                        var sample=await Docker(args.ToArray());lock(resourceSamples)resourceSamples.Add(sample);
                        await Task.Delay(1000,monitorStop.Token);
                    }
                    catch(OperationCanceledException){break;}
                    catch(InvalidOperationException){break;}
                }
            });
        }
        catch { await DisposeAsync(); throw; }
    }
    private async Task<string> Start(string image, int port, int memory, Dictionary<string,string> env, params string[] command)
    {
        var name = "cloud-dms-task12-" + Guid.NewGuid().ToString("N");
        var file = Path.Combine(Path.GetTempPath(), name + ".env");
        try
        {
            await File.WriteAllLinesAsync(file, env.Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false));
            var args = new List<string> { "run", "--detach", "--rm", "--name", name, "--cpus", "2", "--memory", memory + "m", "--pids-limit", "128", "--publish", $"127.0.0.1::{port}", "--env-file", file, image };
            args.AddRange(command); await Docker(args.ToArray()); containers.Add(name);
            var mapping = await Docker("port", name, port.ToString());
            return "http://" + mapping.Trim() + "/";
        }
        finally { File.Delete(file); }
    }
    private static async Task Ready(HttpClient client, string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while(true)
        {
            try { using var response = await client.GetAsync(url, timeout.Token); if(response.IsSuccessStatusCode) return; }
            catch(HttpRequestException) { }
            await Task.Delay(250, timeout.Token);
        }
    }
    public async Task DisposeAsync()
    {
        monitorStop.Cancel();if(monitor is not null)await monitor;
        foreach(var name in containers.ToArray()) { try { await Docker("rm", "--force", name); } catch { } }
        containers.Clear();
    }
    public static async Task<string> Docker(params string[] args)
    {
        var si = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach(var arg in args) si.ArgumentList.Add(arg);
        using var p = Process.Start(si)!;
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await p.WaitForExitAsync(deadline.Token);
        if(p.ExitCode != 0) throw new InvalidOperationException("Isolated Docker test resource failed: " + await stderr);
        return (await stdout).Trim();
    }
}
[CollectionDefinition("Search services", DisableParallelization = true)]
public sealed class SearchServicesCollection : ICollectionFixture<MySqlFixture>, ICollectionFixture<RedisFixture>, ICollectionFixture<SearchServicesFixture> { }
