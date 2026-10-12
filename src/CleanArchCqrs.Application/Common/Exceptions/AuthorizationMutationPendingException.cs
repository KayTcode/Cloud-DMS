namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class AuthorizationMutationPendingException : Exception
{
    public AuthorizationMutationPendingException()
        : base("Another authorization mutation is pending for this scope.")
    {
    }
}
