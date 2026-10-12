namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class AuthorizationMutationConflictException : Exception
{
    public AuthorizationMutationConflictException()
        : base("The idempotency key was already used with a different payload.")
    {
    }
}
