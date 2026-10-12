namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IAuthorizationProjectionWriter
{
    Task PublishAsync(Guid operationId, CancellationToken cancellationToken);
}
