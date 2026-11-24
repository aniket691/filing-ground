using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinQuery.Application.Auth;
using FinQuery.Domain.Errors;
using Microsoft.IdentityModel.Tokens;

namespace FinQuery.Infrastructure.Auth;

public sealed class JwtAccessTokenService(AuthSettings settings) : IAccessTokenIssuer, IAccessTokenReader
{
    private readonly JwtSecurityTokenHandler _handler = CreateHandler();

    public string Create(int userId)
    {
        var key = SigningKey();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]),
            Expires = DateTime.UtcNow.AddMinutes(settings.AccessTokenExpireMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        var token = _handler.CreateToken(descriptor);
        return _handler.WriteToken(token);
    }

    public int ReadUserId(string token)
    {
        if (string.IsNullOrWhiteSpace(settings.SecretKey) || !string.Equals(settings.Algorithm, "HS256", StringComparison.OrdinalIgnoreCase))
        {
            throw new TokenReadException(TokenReadFailure.NotConfigured);
        }

        try
        {
            var principal = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SigningKey(),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = JwtRegisteredClaimNames.Sub
            }, out _);

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(sub, out var userId))
            {
                throw new TokenReadException(TokenReadFailure.MissingSubject);
            }

            return userId;
        }
        catch (TokenReadException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new TokenReadException(TokenReadFailure.Invalid);
        }
    }

    private SymmetricSecurityKey SigningKey()
    {
        if (string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new ValidationError("Authentication is not configured. Set AUTH_SECRET_KEY.");
        }

        if (!string.Equals(settings.Algorithm, "HS256", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationError("Authentication is not configured. Set AUTH_SECRET_KEY.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
    }

    private static JwtSecurityTokenHandler CreateHandler()
    {
        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        handler.InboundClaimTypeMap.Clear();
        return handler;
    }
}
