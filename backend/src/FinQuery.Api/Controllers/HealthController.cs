using FinQuery.Application.Health;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
public sealed class HealthController(IHealthStatusReader health) : ControllerBase
{
    [HttpGet("/health/live")]
    public ActionResult<LiveReport> Live() => Ok(new LiveReport("alive"));

    [HttpGet("/health")]
    public ActionResult<HealthReport> Get() => Ok(health.Read());
}
