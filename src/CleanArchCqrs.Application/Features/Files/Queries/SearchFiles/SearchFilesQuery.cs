using MediatR;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;

public sealed record SearchFilesQuery(string Query, string Mode = "hybrid", Guid? FolderId = null,
    int PageSize = 20, string? Cursor = null) : IRequest<SearchFilesResponse>, ISensitiveReadRequest;
public sealed record SearchFileItem(Guid FileId, string Name, string ContentType, long SizeBytes,
    long DocumentVersion, double Score, IReadOnlyList<string> Sources, string? Snippet);
public sealed record SearchFilesResponse(IReadOnlyList<SearchFileItem> Items, string? NextCursor,
    string ModeUsed, bool Degraded, IReadOnlyList<string> Warnings);

public sealed class InvalidSearchRequestException : Exception;
public sealed class SearchUnavailableException : Exception;
