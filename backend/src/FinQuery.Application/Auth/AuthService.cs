using FinQuery.Domain.Errors;
using FinQuery.Domain.Users;

namespace FinQuery.Application.Auth;

public sealed class AuthService(IUserRepository users, IPasswordHasher passwords, IAccessTokenIssuer tokens)
{
    public const string DuplicateEmailMessage = "An account with this email already exists.";
    public const string InvalidLoginMessage = "Invalid email or password.";

    public async Task<UserResponse> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        var existing = await users.FindByEmailAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictError(DuplicateEmailMessage);
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = normalized,
            HashedPassword = passwords.Hash(password),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await users.AddAsync(user, cancellationToken);
        }
        catch (EmailAlreadyExistsException)
        {
            throw new ConflictError(DuplicateEmailMessage);
        }

        return ToResponse(user);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        var user = await users.FindByEmailAsync(normalized, cancellationToken);
        var passwordOk = user is not null && passwords.Verify(password, user.HashedPassword);
        if (user is null || !passwordOk || !user.IsActive)
        {
            throw new AuthenticationError(InvalidLoginMessage);
        }

        var token = tokens.Create(user.Id);
        return new LoginResult(token, "bearer", ToResponse(user));
    }

    public async Task<UserResponse> RequireActiveUserAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AccessDeniedException(401, "User inactive or not found");
        }

        return ToResponse(user);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static UserResponse ToResponse(User user) => new(user.Id, user.Email, user.IsActive);
}

/// <summary>Repository translation of a unique-email collision. Not an HTTP error.</summary>
public sealed class EmailAlreadyExistsException : Exception;
