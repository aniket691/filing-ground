using FinQuery.Domain.Errors;
using FinQuery.Domain.Users;

namespace FinQuery.Application.Auth;

public sealed class AuthSettings
{
    public string SecretKey { get; init; } = "";

    public string Algorithm { get; init; } = "HS256";

    public int AccessTokenExpireMinutes { get; init; } = 30;

    public int BcryptRounds { get; init; } = 12;
}

public sealed record UserResponse(int Id, string Email, bool IsActive);

public sealed record LoginResult(string AccessToken, string TokenType, UserResponse User);

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);
}

public interface IPasswordHasher
{
    /// <summary>
    /// Returns a bcrypt hash. Throws <see cref="ValidationError"/> when the
    /// password is empty or longer than 72 UTF-8 bytes.
    /// </summary>
    string Hash(string password);

    /// <summary>False for a mismatch, a malformed hash, or an unusable password.</summary>
    bool Verify(string password, string hashedPassword);
}

public interface IAccessTokenIssuer
{
    /// <summary>
    /// HS256 JWT with <c>sub</c> set to the user id string. Throws
    /// <see cref="ValidationError"/> when the signing secret is missing.
    /// </summary>
    string Create(int userId);
}

public enum TokenReadFailure
{
    NotConfigured,
    Invalid,
    MissingSubject
}

public sealed class TokenReadException : Exception
{
    public TokenReadException(TokenReadFailure failure) : base(failure.ToString())
    {
        Failure = failure;
    }

    public TokenReadFailure Failure { get; }
}

public interface IAccessTokenReader
{
    int ReadUserId(string token);
}

/// <summary>
/// Bearer failures that FastAPI reports as <c>{"detail": "..."}</c> with no
/// <c>error</c> field. These are HTTPException cases, not domain errors.
/// </summary>
public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException(int statusCode, string detail) : base(detail)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
