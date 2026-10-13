using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IFileAccessContextResolver
{
    Task<FileAccessContext> ResolveContextForMutationReceiptAsync(CancellationToken cancellationToken);
    Task<FileAccessContext> ResolveContextAsync(CancellationToken cancellationToken);
}
