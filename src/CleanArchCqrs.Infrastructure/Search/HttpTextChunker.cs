using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.Infrastructure.Search;

public sealed class HttpTextChunker(HttpClient client, IOptions<DocumentIndexOptions> options) : ITextChunker, IDisposable
{
    public async Task<ChunkingResult> ChunkAsync(IReadOnlyList<TextSection> sections, CancellationToken ct)
    {
        if (sections.Count > 1000 || sections.Sum(s => (long)s.Text.Length) > DocumentIndexOptions.MaxCharacters
            || sections.Any(s => s.Location?.Length > 500)) throw new ArgumentException("TOKENIZER_REQUEST_LIMIT");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        ct = deadline.Token;
        // Internal service uses the pinned real tokenizer; there is no heuristic fallback.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.TokenizerEndpoint), "chunk"))
        {
            Content = JsonContent.Create(new { modelVersion = options.Value.ModelVersion, sections, targetTokens = 300, overlapTokens = 50,
                maxInputTokens = 512, maxChunks = DocumentIndexOptions.MaxChunks, prefix = "passage: " })
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        // Bound response before JSON allocation. No document text enters logs.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(bytes, ct)) != 0)
        {
            if (buffer.Length + read > 128L * 1024 * 1024) throw new InvalidDataException("TOKENIZER_RESPONSE_LIMIT");
            await buffer.WriteAsync(bytes.AsMemory(0, read), ct);
        }
        buffer.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<ChunkingResult>(buffer,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), ct) ?? throw new InvalidDataException("TOKENIZER_EMPTY");
        if (result.ModelVersion != options.Value.ModelVersion || string.IsNullOrWhiteSpace(result.ChunkerVersion)
            || result.ChunkerVersion.Length > 128 || result.Chunks.Count > DocumentIndexOptions.MaxChunks
            || (result.IsPartial && string.IsNullOrWhiteSpace(result.Reason))
            || result.Chunks.Where((c, i) => c.Ordinal != i || c.TokenCount is < 1 or > 512
                || string.IsNullOrWhiteSpace(c.Text) || c.Location?.Length > 500).Any()
            || (sections.Any(s => !string.IsNullOrWhiteSpace(s.Text)) && result.Chunks.Count == 0))
            throw new InvalidDataException("TOKENIZER_CONTRACT");
        return result;
    }
    public void Dispose() => client.Dispose();
}
