using CleanArchCqrs.Application.Common.Authorization;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IAuthorizationMutationCoordinator
{
    Task<MutationReceipt> ExecuteAsync(
        MutationRequest request,
        Guid initiatedByUserId,
        Func<CancellationToken, Task> persistChange,
        CancellationToken cancellationToken);

    Task<MutationReceipt?> GetAsync(
        Guid operationId,
        Guid requesterUserId,
        CancellationToken cancellationToken);
}
