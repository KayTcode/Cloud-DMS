using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public string HashPassword(string password)
    {
        return Hash(password);
    }

    public bool Verify(string password, string hashedPassword)
    {
        return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        return Verify(password, hashedPassword);
    }
}
