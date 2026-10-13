using CleanArchCqrs.Domain.Entities;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IQuotaReleaseWriter
{
    // Called inside the lifecycle coordinator's database transaction.
    Task ReleaseAsync(FileEntry file, CancellationToken cancellationToken);
}
