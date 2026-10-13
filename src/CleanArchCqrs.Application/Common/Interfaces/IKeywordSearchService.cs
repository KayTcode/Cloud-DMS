using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IKeywordSearchService
{
    Task<SearchBranchResult> SearchAsync(FileAccessContext context, SearchRequest request,
        int offset, CancellationToken cancellationToken);
}
