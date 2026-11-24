using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using FinQuery.Application.Stats;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
public sealed class DashboardStatsController(
    IDashboardStatsReader stats,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpGet("api/v1/stats/dashboard")]
    [HttpGet("stats/dashboard")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await stats.ReadAsync(user.Id, cancellationToken));
    }
}
