using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ISearchSessionStore
{
    Task SaveAsync(SearchSession session, CancellationToken cancellationToken);
    Task<SearchSession?> ReadAsync(Guid id, CancellationToken cancellationToken);
    string Protect(ProtectedSearchCursor cursor);
    ProtectedSearchCursor Unprotect(string cursor);
}
