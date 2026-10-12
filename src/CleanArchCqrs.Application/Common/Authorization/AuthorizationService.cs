using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Application.Common.Authorization;

public sealed class AuthorizationService(
    IAccessSnapshotStore accessSnapshotStore,
    AccessPolicyEvaluator accessPolicyEvaluator) : IAuthorizationService
{
    public async Task<AuthorizationResult> CheckAsync(
        ActorIdentity actor,
        string action,
        AuthorizationTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = await accessSnapshotStore.ReadAsync(actor, target, cancellationToken);
            return accessPolicyEvaluator.Evaluate(input, action);
        }
        catch (SecurityUnavailableException)
        {
            return new AuthorizationResult(AuthorizationDecision.Unavailable);
        }
    }

    public async Task AssertStillValidAsync(
        ActorIdentity actor,
        AuthorizationStamp stamp,
        CancellationToken cancellationToken)
    {
        var input = await accessSnapshotStore.ReadAsync(actor, stamp.Target, cancellationToken);
        var result = accessPolicyEvaluator.Evaluate(input, stamp.Action);
        if (result.Decision == AuthorizationDecision.Unavailable)
        {
            throw new SecurityUnavailableException();
        }

        var currentStamp = result.Stamp;
        if (result.Decision != AuthorizationDecision.Allow || currentStamp is null)
        {
            throw new AuthorizationChangedException();
        }

        if (!string.Equals(currentStamp.Generation, stamp.Generation, StringComparison.Ordinal)
            || !string.Equals(currentStamp.Action, stamp.Action, StringComparison.Ordinal)
            || currentStamp.SessionId != stamp.SessionId
            || currentStamp.UserVersion != stamp.UserVersion
            || currentStamp.SessionVersion != stamp.SessionVersion
            || !currentStamp.ScopeVersions.SequenceEqual(stamp.ScopeVersions)
            || !Equals(currentStamp.Target, stamp.Target)
            || currentStamp.ResourceVersion != stamp.ResourceVersion)
        {
            throw new AuthorizationChangedException();
        }
    }
}
