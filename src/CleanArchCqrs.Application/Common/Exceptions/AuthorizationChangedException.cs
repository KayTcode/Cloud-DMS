namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class AuthorizationChangedException : Exception
{
    public AuthorizationChangedException()
        : base("Authorization state changed before the operation could continue.")
    {
    }
}
