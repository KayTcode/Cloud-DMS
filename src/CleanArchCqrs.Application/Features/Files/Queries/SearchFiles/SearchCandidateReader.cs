using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;

public sealed class SearchCandidateReader(IApplicationDbContext db, IFileAclQuery acl)
{
    public async Task<IReadOnlyList<SearchCandidate>> CurrentAsync(FileAccessContext context,
        Guid? folderId, IReadOnlyList<SearchCandidate> candidates, CancellationToken ct)
    {
        var scope = await acl.ReadScopeAsync(context, DateTimeOffset.UtcNow, ct);
        if (folderId.HasValue) scope = scope.Where(f => f.FolderId == folderId);
        var valid = new List<SearchCandidate>();
        foreach (var batch in candidates.Chunk(200))
        {
            var ids = batch.Select(c => c.FileId).Distinct().ToArray();
            var files = await scope.Where(f => ids.Contains(f.Id))
                .Select(f => new { f.Id, f.DocumentVersion, f.ActiveSearchGenerationId }).ToArrayAsync(ct);
            var activeIds = files.Where(f => f.ActiveSearchGenerationId.HasValue)
                .Select(f => f.ActiveSearchGenerationId!.Value).ToArray();
            var generations = await db.FileSearchGenerations.IgnoreQueryFilters().AsNoTracking()
                .Where(g => g.TenantId == context.TenantId && ids.Contains(g.FileId)
                    && activeIds.Contains(g.Id)
                    && g.ModelVersion == SearchPolicy.Model
                    && (g.Status == FileSearchStatus.Ready || g.Status == FileSearchStatus.Partial))
                .Select(g => new { g.Id, g.FileId, g.DocumentVersion }).ToArrayAsync(ct);
            var genIds = generations.Select(g => g.Id).ToArray();
            var chunks = await db.FileTextChunks.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == context.TenantId && genIds.Contains(c.GenerationId))
                .Where(ChunkMatches(batch))
                .Select(c => new { c.GenerationId, c.Ordinal }).ToArrayAsync(ct);
            foreach (var candidate in batch)
            {
                var file = files.FirstOrDefault(f => f.Id == candidate.FileId && f.DocumentVersion == candidate.DocumentVersion);
                if (file is null || !double.IsFinite(candidate.Score)) continue;
                if (candidate.Source == "keyword" && candidate.GenerationId is null && candidate.ChunkOrdinal is null)
                { valid.Add(candidate); continue; }
                if (candidate.Source is not ("keyword" or "semantic") || candidate.GenerationId is null
                    || candidate.ChunkOrdinal is null || candidate.ChunkOrdinal < 0
                    || candidate.Source == "semantic" && candidate.ModelVersion != SearchPolicy.Model) continue;
                if (file.ActiveSearchGenerationId == candidate.GenerationId
                    && generations.Any(g => g.Id == candidate.GenerationId && g.FileId == file.Id && g.DocumentVersion == file.DocumentVersion)
                    && chunks.Any(c => c.GenerationId == candidate.GenerationId && c.Ordinal == candidate.ChunkOrdinal)) valid.Add(candidate);
            }
        }
        return valid;
    }

    private static Expression<Func<FileTextChunk, bool>> ChunkMatches(IEnumerable<SearchCandidate> candidates)
    {
        var chunk = Expression.Parameter(typeof(FileTextChunk), "chunk");
        Expression predicate = Expression.Constant(false);
        foreach (var match in candidates.Where(c => c.GenerationId.HasValue && c.ChunkOrdinal.HasValue)
            .Select(c => (Generation: c.GenerationId!.Value, Ordinal: c.ChunkOrdinal!.Value)).Distinct())
        {
            var generation = match.Generation;
            var ordinal = match.Ordinal;
            // Captured values become SQL parameters, not candidate IDs embedded in command text/logs.
            Expression<Func<FileTextChunk, bool>> clause = c => c.GenerationId == generation && c.Ordinal == ordinal;
            predicate = Expression.OrElse(predicate, new ReplaceParameter(clause.Parameters[0], chunk).Visit(clause.Body)!);
        }
        return Expression.Lambda<Func<FileTextChunk, bool>>(predicate, chunk);
    }

    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : node;
    }

    // Called only after CheckMany granted this page.
    public async Task<SearchFileItem> ReadItemAsync(FileAccessContext context, RankedSearchFile candidate, CancellationToken ct)
    {
        var file = await db.FileEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.TenantId == context.TenantId && f.Id == candidate.FileId && f.DocumentVersion == candidate.DocumentVersion)
            .Select(f => new { f.Name, f.ContentType, f.SizeBytes }).SingleAsync(ct);
        var match = candidate.Evidence.FirstOrDefault(e => e.GenerationId.HasValue && e.ChunkOrdinal.HasValue);
        string? snippet = null;
        if (match is not null)
        {
            snippet = await db.FileTextChunks.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == context.TenantId && c.GenerationId == match.GenerationId && c.Ordinal == match.ChunkOrdinal)
                .Select(c => c.Text.Substring(0, Math.Min(c.Text.Length, 320))).SingleOrDefaultAsync(ct);
            if (snippet is not null) snippet = new string(snippet.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        }
        return new(candidate.FileId, file.Name, file.ContentType, file.SizeBytes, candidate.DocumentVersion,
            candidate.Score, candidate.Evidence.Select(e => e.Source).Distinct().Order().ToArray(), snippet);
    }
}
