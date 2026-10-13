namespace CleanArchCqrs.Application.Common.Interfaces;

public sealed record TextSection(string Text, string? Location);
public sealed record TextExtractionResult(string ContentType, IReadOnlyList<TextSection> Sections,
    bool IsPartial = false, string? Reason = null);

public interface IFileTextExtractor
{
    // Input is the local bounded, fully scanned object. Type is detected from bytes, never upload headers.
    Task<TextExtractionResult> ExtractAsync(Stream content, string fileName, CancellationToken ct);
}
