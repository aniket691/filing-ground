using System.Text.Json;
using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var (email, password) = AuthRequestReader.ReadRegister(body);
        var user = await auth.RegisterAsync(email, password, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var (email, password) = AuthRequestReader.ReadLogin(body);
        var result = await auth.LoginAsync(email, password, cancellationToken);
        return Ok(new
        {
            access_token = result.AccessToken,
            token_type = result.TokenType,
            user = result.User
        });
    }
}
