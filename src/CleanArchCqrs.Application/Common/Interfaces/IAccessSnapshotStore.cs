using CleanArchCqrs.Application.Common.Authorization;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IAccessSnapshotStore
{
    Task<AccessEvaluationInput> ReadAsync(
        ActorIdentity actor,
        AuthorizationTarget target,
        CancellationToken cancellationToken);
}
