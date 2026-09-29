namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hashedPassword);

    string HashPassword(string password) => Hash(password);
    bool VerifyPassword(string password, string hashedPassword) => Verify(password, hashedPassword);
}
