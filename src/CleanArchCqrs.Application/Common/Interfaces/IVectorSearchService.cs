using CleanArchCqrs.Application.Common.Models.Files;
namespace CleanArchCqrs.Application.Common.Interfaces;
public interface IVectorSearchService
{
    // Candidates only. Caller must validate current ACL/state/version/generation in SQL before disclosure.
    Task<SearchBranchResult> SearchAsync(FileAccessContext context, SearchRequest request, float[] vector, int limit, CancellationToken ct);
}
