using CleanArchCqrs.Application.Common.Models.Files;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IObjectReadUrlService
{
    Task<ObjectReadAccess> CreateAsync(ReadUrlRequest request, CancellationToken cancellationToken);
}
