namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class SecurityUnavailableException : Exception
{
    public SecurityUnavailableException()
        : base("Security state is unavailable.")
    {
    }

    public SecurityUnavailableException(Exception innerException)
        : base("Security state is unavailable.", innerException)
    {
    }
}
