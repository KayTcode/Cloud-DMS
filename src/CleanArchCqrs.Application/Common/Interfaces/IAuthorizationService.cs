using CleanArchCqrs.Application.Common.Authorization;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IAuthorizationService
{
    Task<AuthorizationResult> CheckAsync(
        ActorIdentity actor,
        string action,
        AuthorizationTarget target,
        CancellationToken cancellationToken);

    Task AssertStillValidAsync(
        ActorIdentity actor,
        AuthorizationStamp stamp,
        CancellationToken cancellationToken);
}
