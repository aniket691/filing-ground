using System.Text.Json;

namespace FinQuery.Api.Auth;

public sealed record BodyFieldError(string Msg, string[] Loc);

public sealed class BodyValidationException : Exception
{
    public BodyValidationException(string message, string field) : base(message)
    {
        Errors = [new BodyFieldError(message, ["body", field])];
    }

    public IReadOnlyList<BodyFieldError> Errors { get; }
}

public static class AuthRequestReader
{
    public static (string Email, string Password) ReadRegister(JsonElement body) =>
        Read(body, minimumPasswordLength: 8);

    public static (string Email, string Password) ReadLogin(JsonElement body) =>
        Read(body, minimumPasswordLength: 1);

    private static (string Email, string Password) Read(JsonElement body, int minimumPasswordLength)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new BodyValidationException("Request body must be an object.", "body");
        }

        foreach (var property in body.EnumerateObject())
        {
            if (property.Name is not ("email" or "password"))
            {
                throw new BodyValidationException("Extra inputs are not permitted", property.Name);
            }
        }

        if (!body.TryGetProperty("email", out var emailNode) || emailNode.ValueKind != JsonValueKind.String)
        {
            throw new BodyValidationException("value is not a valid email address", "email");
        }

        if (!body.TryGetProperty("password", out var passwordNode) || passwordNode.ValueKind != JsonValueKind.String)
        {
            throw new BodyValidationException("Password must be a string.", "password");
        }

        var email = (emailNode.GetString() ?? "").Trim();
        var password = passwordNode.GetString() ?? "";
        if (!IsEmail(email))
        {
            throw new BodyValidationException("value is not a valid email address", "email");
        }

        if (password.Length < minimumPasswordLength)
        {
            throw new BodyValidationException($"String should have at least {minimumPasswordLength} characters", "password");
        }

        if (password.Length > 72)
        {
            throw new BodyValidationException("String should have at most 72 characters", "password");
        }

        return (email, password);
    }

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@'))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !value.Contains(' ');
    }
}
