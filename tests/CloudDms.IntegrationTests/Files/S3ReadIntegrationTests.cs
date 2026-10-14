using System.Net;
using System.Net.Http.Headers;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Infrastructure.Services.Storage;
using CloudDms.IntegrationTests.Support;
namespace CloudDms.IntegrationTests.Files;
[Collection("Search services")]
public sealed class S3ReadIntegrationTests(SearchServicesFixture services)
{
    [Fact]
    public async Task ActualMinioMultipartPrivateRangeResumeExpiredUrlAndAbort()
    {
        using var storage = new S3StorageService(services.S3Config);
        using var http = new HttpClient();
        var key = "objects/" + Guid.NewGuid().ToString("N");
        var upload = await storage.BeginAsync(key, "text/plain", default);
        var first = Enumerable.Repeat((byte)65, 5 * 1024 * 1024).ToArray();
        var last = Enumerable.Repeat((byte)66, 4096).ToArray();
        var parts = new List<MultipartPart>();
        foreach(var (number, bytes) in new[] { (1,first), (2,last) })
        {
            var uri = await storage.PresignPartAsync(key, upload, number, TimeSpan.FromMinutes(2), default);
            using var response = await http.PutAsync(uri, new ByteArrayContent(bytes));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            parts.Add(new(number, response.Headers.ETag!.ToString()));
        }
        await storage.CompleteAsync(key, upload, parts, default);
        Assert.Equal(first.Length + last.Length, (await storage.InspectAsync(key, default))!.SizeBytes);
        using(var anonymous = await http.GetAsync(services.S3Endpoint + "search-test/" + key))
            Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
        var access = await storage.CreateReadUrlAsync(key, "../ngân sách\r\n.pdf", "text/plain", "download", TimeSpan.FromMinutes(2), default);
        Assert.True(access.SupportsRange);
        using(var request = new HttpRequestMessage(HttpMethod.Get, access.Url))
        {
            request.Headers.Range = new RangeHeaderValue(first.Length - 2, first.Length + 2);
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(new byte[] {65,65,66,66,66}, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
            Assert.DoesNotContain("\r", response.Content.Headers.ContentDisposition.ToString());
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        }
        using(var request = new HttpRequestMessage(HttpMethod.Get, access.Url))
        {
            request.Headers.Range = new RangeHeaderValue(first.Length, null);
            using var response = await http.SendAsync(request);
            Assert.Equal(last, await response.Content.ReadAsByteArrayAsync());
        }
        var expires = await storage.CreateReadUrlAsync(key, "x.txt", "text/plain", "download", TimeSpan.FromSeconds(1), default);
        await Task.Delay(TimeSpan.FromSeconds(3));
        using(var expired = await http.GetAsync(expires.Url)) Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        var abandonedKey = "objects/" + Guid.NewGuid().ToString("N");
        var abandoned = await storage.BeginAsync(abandonedKey, "text/plain", default);
        await storage.AbortAsync(abandonedKey, abandoned, default);
        Assert.Null(await storage.InspectAsync(abandonedKey, default));
        Assert.True(await storage.DeleteFileAsync(key));
    }
}
