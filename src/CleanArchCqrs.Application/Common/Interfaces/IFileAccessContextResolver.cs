using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IFileAccessContextResolver
{
    Task<FileAccessContext> ResolveContextAsync(CancellationToken cancellationToken);
}
