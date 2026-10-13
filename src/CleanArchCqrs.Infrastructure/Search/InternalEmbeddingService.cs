using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using Microsoft.Extensions.Options;
namespace CleanArchCqrs.Infrastructure.Search;
public sealed class InternalEmbeddingService(HttpClient client, IOptions<EmbeddingOptions> options) : IEmbeddingService, IDisposable
{
    public void Dispose() => client.Dispose();
    public async Task<EmbeddingResult> EmbedQueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1000) throw new ArgumentException("QUERY_LIMIT");
        return (await EmbedAsync([query], "query", ct))[0];
    }
    public async Task<IReadOnlyList<EmbeddingResult>> EmbedPassagesAsync(IReadOnlyList<string> passages, CancellationToken ct)
    {
        if (passages.Count > DocumentIndexOptions.MaxChunks) throw new ArgumentException("PASSAGE_LIMIT");
        var result = new List<EmbeddingResult>(passages.Count);
        foreach (var batch in passages.Chunk(options.Value.BatchSize)) result.AddRange(await EmbedAsync(batch, "passage", ct));
        return result;
    }
    private async Task<IReadOnlyList<EmbeddingResult>> EmbedAsync(IReadOnlyList<string> texts, string kind, CancellationToken ct)
    {
        if (texts.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 32768)) throw new ArgumentException("TEXT_LIMIT");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.Endpoint), "embed"))
        { Content = JsonContent.Create(new { modelVersion = options.Value.ModelVersion, kind, texts }) };
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        using var json = await SearchHttp.ReadJsonAsync(response, 1024 * 1024, deadline.Token);
        var root = json.RootElement;
        if (root.GetProperty("modelVersion").GetString() != options.Value.ModelVersion || root.GetProperty("dimensions").GetInt32() != EmbeddingOptions.Dimensions)
            throw new InvalidDataException("EMBEDDING_MODEL_CONTRACT");
        var vectors = root.GetProperty("vectors").Deserialize<float[][]>() ?? throw new InvalidDataException("EMBEDDING_EMPTY");
        if (vectors.Length != texts.Count) throw new InvalidDataException("EMBEDDING_COUNT");
        foreach (var vector in vectors) EmbeddingOptions.ValidateVector(vector);
        return vectors.Select(v => new EmbeddingResult(v, options.Value.ModelVersion)).ToArray();
    }
}
internal static class SearchHttp
{
    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new InvalidDataException("SEARCH_RESPONSE_LIMIT");
            buffer.Write(bytes, 0, read);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }
}
