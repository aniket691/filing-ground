using FinQuery.Application.Auth;

namespace FinQuery.Api.Auth;

public static class CurrentUser
{
    public static async Task<UserResponse> RequireAsync(
        HttpRequest request,
        IAccessTokenReader tokens,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            throw new AccessDeniedException(StatusCodes.Status401Unauthorized, "Missing authentication token");
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
        {
            throw new AccessDeniedException(StatusCodes.Status401Unauthorized, "Missing authentication token");
        }

        try
        {
            var userId = tokens.ReadUserId(token);
            return await auth.RequireActiveUserAsync(userId, cancellationToken);
        }
        catch (TokenReadException exception)
        {
            throw exception.Failure switch
            {
                TokenReadFailure.NotConfigured => new AccessDeniedException(
                    StatusCodes.Status503ServiceUnavailable,
                    "Authentication is not configured"),
                TokenReadFailure.MissingSubject => new AccessDeniedException(
                    StatusCodes.Status401Unauthorized,
                    "Missing user identifier in token"),
                _ => new AccessDeniedException(StatusCodes.Status401Unauthorized, "Invalid authentication token")
            };
        }
    }
}
