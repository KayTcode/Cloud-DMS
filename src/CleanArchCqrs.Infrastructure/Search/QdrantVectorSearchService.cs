using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using Microsoft.Extensions.Options;
namespace CleanArchCqrs.Infrastructure.Search;

public sealed class QdrantVectorSearchService(HttpClient client, IOptions<QdrantOptions> options,
    IOptions<EmbeddingOptions> embedding) : IVectorSearchService, IDisposable
{
    public void Dispose() => client.Dispose();
    public async Task<SearchBranchResult> SearchAsync(FileAccessContext context, SearchRequest request, float[] vector, int limit, CancellationToken ct)
    {
        if (context.TenantId == Guid.Empty || request.ModelVersion != embedding.Value.ModelVersion || limit is < 1 or > 1000)
            throw new ArgumentException("VECTOR_SEARCH_CONTRACT");
        EmbeddingOptions.ValidateVector(vector);
        var collection = QdrantOptions.Collection(request.ModelVersion);
        await EnsureCollectionAsync(collection, false, ct);
        using var json = await SendAsync(HttpMethod.Post, $"collections/{collection}/points/query", new
        {
            query = vector, limit, with_payload = true, with_vector = false,
            filter = Filter(context.TenantId, null, null, request.ModelVersion)
        }, ct);
        var candidates = new List<SearchCandidate>();
        foreach (var point in json.RootElement.GetProperty("result").GetProperty("points").EnumerateArray())
        {
            var p = point.GetProperty("payload");
            // Defense against a malformed backend response; SQL remains the authorization authority.
            if (p.GetProperty("tenantId").GetGuid() != context.TenantId || p.GetProperty("modelVersion").GetString() != request.ModelVersion)
                throw new InvalidDataException("VECTOR_TENANT_MODEL_CONTRACT");
            var score = point.GetProperty("score").GetDouble();
            var ordinal = p.GetProperty("chunkOrdinal").GetInt32();
            var version = p.GetProperty("documentVersion").GetInt64();
            if (!double.IsFinite(score) || ordinal < 0 || version < 1 || candidates.Count >= limit)
                throw new InvalidDataException("VECTOR_CANDIDATE_CONTRACT");
            candidates.Add(new(p.GetProperty("fileId").GetGuid(), version, p.GetProperty("generationId").GetGuid(), ordinal, score, "semantic", request.ModelVersion));
        }
        return new(candidates, candidates.Count == limit);
    }

    internal async Task EnsureCollectionAsync(string collection, bool create, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Get, $"collections/{collection}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound && create)
        {
            using var created = await SendRawAsync(HttpMethod.Put, $"collections/{collection}",
                new { vectors = new { size = EmbeddingOptions.Dimensions, distance = "Cosine" }, on_disk_payload = true }, ct);
            // A concurrent creator may win; re-read and validate the actual collection in all cases.
            if (!created.IsSuccessStatusCode && created.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.BadRequest))
                created.EnsureSuccessStatusCode();
            await EnsureCollectionAsync(collection, false, ct);
            return;
        }
        response.EnsureSuccessStatusCode();
        using var json = await SearchHttp.ReadJsonAsync(response, 1024 * 1024, ct);
        var vectors = json.RootElement.GetProperty("result").GetProperty("config").GetProperty("params").GetProperty("vectors");
        if (vectors.GetProperty("size").GetInt32() != EmbeddingOptions.Dimensions || vectors.GetProperty("distance").GetString() != "Cosine")
            throw new InvalidDataException("VECTOR_COLLECTION_CONTRACT");
    }

    internal async Task UpsertAsync(string collection, object[] points, CancellationToken ct)
    {
        using var json = await SendAsync(HttpMethod.Put, $"collections/{collection}/points?wait=true", new { points }, ct);
        RequireCompleted(json);
    }
    internal async Task DeleteAsync(Guid tenantId, Guid fileId, Guid? generationId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || fileId == Guid.Empty || generationId == Guid.Empty) throw new ArgumentException("VECTOR_DELETE_SCOPE");
        using var collections = await SendAsync(HttpMethod.Get, "collections", null, ct);
        foreach (var item in collections.RootElement.GetProperty("result").GetProperty("collections").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith(QdrantOptions.CollectionPrefix, StringComparison.Ordinal)
                || name.Length != QdrantOptions.CollectionPrefix.Length + 64
                || name[QdrantOptions.CollectionPrefix.Length..].Any(c => !char.IsAsciiHexDigit(c))) continue;
            using var response = await SendRawAsync(HttpMethod.Post, $"collections/{name}/points/delete?wait=true",
                new { filter = Filter(tenantId, fileId, generationId, null) }, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            using var json = await SearchHttp.ReadJsonAsync(response, 1024 * 1024, ct);
            RequireCompleted(json);
        }
    }
    private static object Filter(Guid tenant, Guid? file, Guid? generation, string? model)
    {
        var must = new List<object> { Match("tenantId", tenant.ToString("D")) };
        if (file.HasValue) must.Add(Match("fileId", file.Value.ToString("D")));
        if (generation.HasValue) must.Add(Match("generationId", generation.Value.ToString("D")));
        if (model != null) must.Add(Match("modelVersion", model));
        return new { must };
    }
    private static object Match(string key, string value) => new { key, match = new { value } };
    private static void RequireCompleted(JsonDocument json)
    {
        if (json.RootElement.GetProperty("result").GetProperty("status").GetString() != "completed")
            throw new InvalidDataException("VECTOR_WRITE_INCOMPLETE");
    }
    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        response.EnsureSuccessStatusCode();
        return await SearchHttp.ReadJsonAsync(response, 4 * 1024 * 1024, ct);
    }
    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(new Uri(options.Value.Endpoint), path));
        if (!string.IsNullOrEmpty(options.Value.ApiKey)) message.Headers.Add("api-key", options.Value.ApiKey);
        if (body != null) message.Content = JsonContent.Create(body);
        // ResponseContentRead makes HttpClient's timeout cover the bounded response body as well.
        return await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct);
    }
}
