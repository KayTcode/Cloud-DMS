using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Enums;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Search;

public sealed class MySqlKeywordSearchService(AppDbContext db, IFileAclQuery acl) : IKeywordSearchService
{
    private const int MaxCandidates = 1000;
    private const string Source = "keyword";

    public async Task<SearchBranchResult> SearchAsync(FileAccessContext context, SearchRequest request,
        int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (offset < 0 || offset >= MaxCandidates)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (request.CandidateLimit is < 1 or > MaxCandidates || request.CandidateLimit > MaxCandidates - offset)
            throw new ArgumentOutOfRangeException(nameof(request.CandidateLimit));
        var query = request.Query?.Trim() ?? "";
        if (query.Length is < 1 or > 1000)
            throw new ArgumentException("Search query must contain 1 to 1000 characters.", nameof(request));

        var scope = await new FileSearchSqlScope(acl).ReadAsync(context, request.FolderId, cancellationToken);
        var window = Math.Min(MaxCandidates, offset + request.CandidateLimit + 1);
        var pattern = "%" + EscapeLike(query) + "%";
        var shortToken = query.Length < 3;

        // MATCH arguments are EF parameters. LIKE explicitly escapes user wildcards.
        var metadata = await scope
            .Select(file => new
            {
                file.Id, file.DocumentVersion, file.ActiveSearchGenerationId,
                LikeName = EF.Functions.Like(file.Name, pattern, "\\"),
                FullTextScore = shortToken ? 0.0 : EF.Functions.Match(
                    file.Name, query, MySqlMatchSearchMode.NaturalLanguage)
            })
            .Where(hit => hit.LikeName || hit.FullTextScore > 0)
            .OrderByDescending(hit => (hit.LikeName ? 10.0 : 0.0) + hit.FullTextScore)
            .ThenBy(hit => hit.Id)
            .Take(window)
            .ToArrayAsync(cancellationToken);

        var currentText =
            from file in scope
            join generation in db.FileSearchGenerations.IgnoreQueryFilters().AsNoTracking()
                on new { file.TenantId, FileId = file.Id, file.DocumentVersion, GenerationId = file.ActiveSearchGenerationId }
                equals new { generation.TenantId, generation.FileId, generation.DocumentVersion,
                    GenerationId = (Guid?)generation.Id }
            join chunk in db.FileTextChunks.IgnoreQueryFilters().AsNoTracking()
                on new { generation.TenantId, GenerationId = generation.Id }
                equals new { chunk.TenantId, chunk.GenerationId }
            where generation.Status == FileSearchStatus.Ready || generation.Status == FileSearchStatus.Partial
            select new { file, generation, chunk };

        var content = await currentText
            .Select(hit => new
            {
                FileId = hit.file.Id, hit.file.DocumentVersion, GenerationId = hit.generation.Id,
                hit.chunk.Ordinal,
                Score = shortToken
                    ? (EF.Functions.Like(hit.chunk.Text, pattern, "\\") ? 1.0 : 0.0)
                    : EF.Functions.Match(hit.chunk.Text, query, MySqlMatchSearchMode.NaturalLanguage)
            })
            .Where(hit => hit.Score > 0)
            .GroupBy(hit => new { hit.FileId, hit.DocumentVersion, hit.GenerationId })
            .Select(group => new
            {
                group.Key.FileId, group.Key.DocumentVersion, group.Key.GenerationId,
                Score = group.Max(hit => hit.Score), Ordinal = group.Min(hit => hit.Ordinal)
            })
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.FileId)
            .Take(window)
            .ToArrayAsync(cancellationToken);

        var merged = metadata.Select(hit => new SearchCandidate(hit.Id, hit.DocumentVersion,
                null, null, (hit.LikeName ? 10.0 : 0.0) + hit.FullTextScore,
                Source, null))
            .Concat(content.Select(hit => new SearchCandidate(hit.FileId, hit.DocumentVersion,
                hit.GenerationId, hit.Ordinal, hit.Score, Source, null)))
            .GroupBy(hit => hit.FileId)
            .Select(group => group.OrderByDescending(hit => hit.Score).ThenBy(hit => hit.ChunkOrdinal).First())
            .OrderByDescending(hit => hit.Score).ThenBy(hit => hit.FileId)
            .ToArray();

        var page = merged.Skip(offset).Take(request.CandidateLimit).ToArray();
        return new SearchBranchResult(page, merged.Length > offset + page.Length);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\")
        .Replace("%", "\\%").Replace("_", "\\_");
}
