using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using MediatR;

namespace CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;

public sealed class SearchFilesQueryHandler(IFileAccessContextResolver resolver, IFileAccessService access,
    IKeywordSearchService keyword, IEmbeddingService embedding, IVectorSearchService vector,
    ISearchSessionStore sessions, SearchCandidateReader reader) : IRequestHandler<SearchFilesQuery, SearchFilesResponse>
{
    public async Task<SearchFilesResponse> Handle(SearchFilesQuery request, CancellationToken cancellationToken)
    {
        if (request.Query is null || request.Query.Length > 1000 || string.IsNullOrWhiteSpace(request.Query)
            || request.PageSize is < 1 or > 100 || request.Mode is not ("keyword" or "semantic" or "hybrid")
            || request.Cursor?.Length > 4096) throw new InvalidSearchRequestException();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var context = await resolver.ResolveContextAsync(ct);
        var query = request.Query.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { Query = query, request.Mode, request.FolderId, request.PageSize, Model = SearchPolicy.Model }))));
        SearchSession session;
        var position = 0;
        if (request.Cursor is not null)
        {
            var cursor = sessions.Unprotect(request.Cursor);
            if (cursor.UserId != context.Actor.UserId || cursor.ActorSessionId != context.Actor.SessionId
                || cursor.TenantId != context.TenantId || cursor.RequestHash != hash || cursor.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidSearchRequestException();
            session = await sessions.ReadAsync(cursor.SessionId, ct) ?? throw new InvalidSearchRequestException();
            if (session.UserId != cursor.UserId || session.ActorSessionId != cursor.ActorSessionId
                || session.TenantId != cursor.TenantId || session.RequestHash != hash || session.ExpiresAt != cursor.ExpiresAt
                || session.ExpiresAt <= DateTimeOffset.UtcNow || cursor.Position < 0 || cursor.Position >= session.Candidates.Count)
                throw new InvalidSearchRequestException();
            position = cursor.Position;
        }
        else
        {
            session = await CreateSessionAsync(context, request, query, hash, ct);
            // A missing/unavailable session cache fails closed even for the first page.
            await sessions.SaveAsync(session, ct);
        }

        var remaining = session.Candidates.Skip(position).ToArray();
        var current = (await reader.CurrentAsync(context, request.FolderId,
            remaining.SelectMany(f => f.Evidence).ToArray(), ct)).ToHashSet();
        var grants = await access.CheckManyAsync(context, remaining.Select(f => f.FileId).ToArray(), ct);
        var permitted = remaining.Where(f => f.Evidence.All(current.Contains)
            && grants.TryGetValue(f.FileId, out var grant) && grant.DocumentVersion == f.DocumentVersion)
            .Take(request.PageSize + 1).ToArray();
        var page = permitted.Take(request.PageSize).ToArray();
        var items = new List<SearchFileItem>();
        foreach (var file in page) items.Add(await reader.ReadItemAsync(context, file, ct));

        // Recheck both generation and ACL after reading text. Include look-ahead in this
        // check: nextCursor must not signal the existence of an unauthorized candidate.
        var evidence = permitted.SelectMany(f => f.Evidence).ToArray();
        var final = (await reader.CurrentAsync(context, request.FolderId, evidence, ct)).ToHashSet();
        if (evidence.Any(e => !final.Contains(e))) throw new AuthorizationChangedException();
        await access.AssertStillValidAsync(context, permitted.Select(f => grants[f.FileId]).ToArray(), ct);
        // AssertStillValid has no work for an empty page; the strict resolver still checks session freshness.
        var finalContext = await resolver.ResolveContextAsync(ct);
        if (finalContext != context) throw new AuthorizationChangedException();
        if (session.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidSearchRequestException();
        string? next = null;
        if (permitted.Length > request.PageSize)
        {
            var nextPosition = position + Array.FindIndex(remaining, f => f.FileId == permitted[^1].FileId);
            next = sessions.Protect(new(session.Id, session.UserId, session.ActorSessionId,
                session.TenantId, session.RequestHash, nextPosition, session.ExpiresAt));
        }
        ct.ThrowIfCancellationRequested();
        return new(items, next, session.ModeUsed, session.Degraded,
            session.Degraded ? ["SEMANTIC_UNAVAILABLE"] : []);
    }

    private async Task<SearchSession> CreateSessionAsync(FileAccessContext context, SearchFilesQuery request,
        string query, string hash, CancellationToken ct)
    {
        var raw = new List<SearchCandidate>();
        var degraded = false;
        float[]? values = null;
        // Reserve time for SQL and final authorization when a semantic dependency stalls.
        using var semanticDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        semanticDeadline.CancelAfter(TimeSpan.FromSeconds(4));
        if (request.Mode != "keyword")
        {
            try
            {
                var embedded = await embedding.EmbedQueryAsync(query, semanticDeadline.Token);
                if (embedded.ModelVersion != SearchPolicy.Model) throw new InvalidDataException();
                values = embedded.Values;
            }
            catch (Exception ex) when (SemanticFailure(ex, ct))
            {
                if (request.Mode == "semantic") throw new SearchUnavailableException();
                degraded = true;
            }
        }
        var useKeyword = request.Mode != "semantic";
        var keywordMore = useKeyword;
        var vectorMore = values is not null;
        var budget = SearchPolicy.CandidateBudget;
        var offset = 0;
        var previousVectorLimit = 0;
        // Initial fetch plus at most five refills. Repeated top-k rows also consume budget.
        for (var round = 0; round <= 5 && budget > 0 && (keywordMore || vectorMore); round++)
        {
            ct.ThrowIfCancellationRequested();
            var window = Math.Min(32 << round, budget);
            if (keywordMore)
            {
                var limit = Math.Min(window, vectorMore ? Math.Max(1, budget / 2) : budget);
                var result = await keyword.SearchAsync(context, new(query, request.FolderId, limit, SearchPolicy.Model), offset, ct);
                raw.AddRange(result.Candidates);
                budget -= limit; // Charge requested capacity, even if a dependency returns fewer rows.
                offset += result.Candidates.Count;
                keywordMore = result.HasMore && result.Candidates.Count > 0 && offset < 1000;
            }
            if (vectorMore && budget > previousVectorLimit)
            {
                var limit = Math.Min(window, budget);
                if (limit <= previousVectorLimit) { vectorMore = false; continue; }
                budget -= limit;
                try
                {
                    var result = await vector.SearchAsync(context, new(query, request.FolderId, limit, SearchPolicy.Model),
                        values!, limit, semanticDeadline.Token);
                    raw.AddRange(result.Candidates);
                    vectorMore = result.HasMore;
                    previousVectorLimit = limit;
                }
                catch (Exception ex) when (SemanticFailure(ex, ct))
                {
                    if (request.Mode == "semantic") throw new SearchUnavailableException();
                    degraded = true;
                    vectorMore = false;
                    raw.RemoveAll(c => c.Source == "semantic");
                }
            }
            else vectorMore = false;
        }
        // Qdrant payload is never authoritative. SQL filters tenant, folder, ACL, clean
        // current content, generation/model and ordinal before any branch rank is assigned.
        var current = await reader.CurrentAsync(context, request.FolderId, raw.Distinct().ToArray(), ct);
        var grants = await access.CheckManyAsync(context, current.Select(c => c.FileId).Distinct().ToArray(), ct);
        var ranked = ReciprocalRankFusion.Rank(current.Where(c => grants.TryGetValue(c.FileId, out var grant)
            && grant.DocumentVersion == c.DocumentVersion));
        return new(Guid.NewGuid(), context.Actor.UserId, context.Actor.SessionId, context.TenantId,
            hash, DateTimeOffset.UtcNow.Add(SearchPolicy.Lifetime), degraded ? "keyword" : request.Mode, degraded, ranked);
    }

    // This predicate is used ONLY around embedding/vector calls, never SQL or authorization.
    private static bool SemanticFailure(Exception ex, CancellationToken overall) => !overall.IsCancellationRequested
        && ex is HttpRequestException or IOException or InvalidDataException or JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException or OverflowException or OperationCanceledException;
}
