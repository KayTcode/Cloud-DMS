using CleanArchCqrs.Application.Common.Models.Files;
namespace CleanArchCqrs.Application.Common.Interfaces;
public interface IEmbeddingService
{
    Task<EmbeddingResult> EmbedQueryAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<EmbeddingResult>> EmbedPassagesAsync(IReadOnlyList<string> passages, CancellationToken ct);
}
