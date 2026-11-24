using System.Text;
using FinQuery.Application.Auth;
using FinQuery.Domain.Errors;

namespace FinQuery.Infrastructure.Auth;

public sealed class BcryptPasswordHasher(int rounds) : IPasswordHasher
{
    public const int MaxBytes = 72;

    public string Hash(string password)
    {
        AsPasswordBytes(password);
        if (rounds is < 4 or > 31)
        {
            throw new ValidationError("AUTH_BCRYPT_ROUNDS must be between 4 and 31.");
        }

        return BCrypt.Net.BCrypt.HashPassword(password, rounds);
    }

    public bool Verify(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(password) > MaxBytes)
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[] AsPasswordBytes(string password)
    {
        if (password.Length == 0)
        {
            throw new ValidationError("Password is required.");
        }

        var raw = Encoding.UTF8.GetBytes(password);
        if (raw.Length > MaxBytes)
        {
            throw new ValidationError("Password exceeds the maximum allowed length.");
        }

        return raw;
    }
}
