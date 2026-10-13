using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Interfaces;

public sealed record ChunkingResult(string ModelVersion, string ChunkerVersion,
    IReadOnlyList<ExtractedChunk> Chunks, bool IsPartial, string? Reason);

public interface ITextChunker
{
    Task<ChunkingResult> ChunkAsync(IReadOnlyList<TextSection> sections, CancellationToken ct);
}
