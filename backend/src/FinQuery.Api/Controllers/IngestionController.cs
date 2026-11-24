using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using FinQuery.Application.Ingestion;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
public sealed class IngestionController(
    DocumentProcessService processing,
    IIngestionCounters counters,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpPost("api/v1/documents/{documentId:int}/process")]
    public async Task<IActionResult> Process(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var force = false;
        var rawForce = Request.Query["force"].FirstOrDefault();
        if (!string.IsNullOrEmpty(rawForce))
        {
            if (!bool.TryParse(rawForce, out force))
            {
                throw new BodyValidationException("Input should be a valid boolean", "force");
            }
        }

        var status = await processing.QueueAsync(user.Id, documentId, force, cancellationToken);
        return StatusCode(StatusCodes.Status202Accepted, new { document_id = status.DocumentId, status = "queued" });
    }

    [HttpGet("api/v1/documents/{documentId:int}/status")]
    public async Task<IActionResult> Status(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await processing.GetStatusAsync(user.Id, documentId, cancellationToken));
    }

    [HttpGet("api/v1/ingestion/stats")]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken)
    {
        await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(counters.Snapshot());
    }
}
