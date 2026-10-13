namespace CleanArchCqrs.Application.Common.Models.Files;

public static class SearchPolicy
{
    public const string Model = "intfloat/multilingual-e5-small@614241f622f53c4eeff9890bdc4f31cfecc418b3";
    public const int CandidateBudget = 1000;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}

// IDs, versions and ranking evidence only; never document text, query text or URLs.
public sealed record RankedSearchFile(Guid FileId, long DocumentVersion, double Score,
    IReadOnlyList<SearchCandidate> Evidence);
public sealed record SearchSession(Guid Id, Guid UserId, Guid ActorSessionId, Guid TenantId,
    string RequestHash, DateTimeOffset ExpiresAt, string ModeUsed, bool Degraded,
    IReadOnlyList<RankedSearchFile> Candidates);
public sealed record ProtectedSearchCursor(Guid SessionId, Guid UserId, Guid ActorSessionId,
    Guid TenantId, string RequestHash, int Position, DateTimeOffset ExpiresAt);
