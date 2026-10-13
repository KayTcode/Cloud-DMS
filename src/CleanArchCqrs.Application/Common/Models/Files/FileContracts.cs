using CleanArchCqrs.Application.Common.Authorization;
namespace CleanArchCqrs.Application.Common.Models.Files;

public sealed record FileAccessContext(ActorIdentity Actor, Guid TenantId);
public sealed record FileAccessGrant(Guid FileId, long DocumentVersion, long AuthorizationVersion, AuthorizationStamp Stamp, string AclFingerprint = "");
public sealed record SearchRequest(string Query, Guid? FolderId, int CandidateLimit, string ModelVersion);
public sealed record SearchCandidate(Guid FileId, long DocumentVersion, Guid? GenerationId, int? ChunkOrdinal, double Score, string Source, string? ModelVersion);
public sealed record SearchBranchResult(IReadOnlyList<SearchCandidate> Candidates, bool HasMore);
public sealed record ReadUrlRequest(Guid ProviderId, string ObjectKey, string FileName, string ContentType, string Purpose, TimeSpan Lifetime);
public sealed record ObjectReadAccess(Uri Url, DateTimeOffset ExpiresAt, bool SupportsRange);
public sealed record EmbeddingResult(float[] Values, string ModelVersion);
public sealed record ExtractedChunk(int Ordinal, string Text, int TokenCount, string? Location);
public sealed record SearchCursorState(Guid UserId, Guid TenantId, string QueryHash, Guid SearchSessionId, int Position, DateTimeOffset ExpiresAt);
