using System.Text.Json;
using FinQuery.Api.Auth;
using FinQuery.Application.Legacy;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
public sealed class LegacyRagController(LegacyRagService legacy) : ControllerBase
{
    [HttpPost("/query")]
    public async Task<IActionResult> Query(CancellationToken cancellationToken)
    {
        var question = await ReadQuestionAsync(cancellationToken);
        try
        {
            return Ok(await legacy.QueryAsync(question, cancellationToken));
        }
        catch (LegacyQueryFailure failure)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { detail = failure.Message });
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            if (message.Contains("expired_api_key", StringComparison.Ordinal) || message.Contains("Invalid API Key", StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new
                {
                    detail = "Groq API key is invalid or expired. Update GROQ_API_KEY in .env (https://console.groq.com/keys) and restart the API."
                });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new { detail = message });
        }
    }

    [HttpPost("/ingest")]
    public async Task<IActionResult> Ingest(CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? paths = null;
        if (Request.ContentLength is > 0)
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new BodyValidationException("Input should be a valid dictionary", "body");
            }

            if (document.RootElement.TryGetProperty("file_paths", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                paths = files.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();
            }
        }

        try
        {
            return Ok(legacy.Ingest(paths));
        }
        catch (LegacyQueryFailure failure)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { detail = failure.Message });
        }
        catch (Exception exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { detail = exception.Message });
        }
    }

    [HttpGet("/documents")]
    public IActionResult Documents()
    {
        try
        {
            return Ok(legacy.Documents());
        }
        catch (LegacyQueryFailure failure)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { detail = failure.Message });
        }
    }

    [HttpDelete("/reset")]
    public IActionResult Reset()
    {
        try
        {
            return Ok(legacy.Reset());
        }
        catch (LegacyQueryFailure failure)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { detail = failure.Message });
        }
    }

    private async Task<string> ReadQuestionAsync(CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new BodyValidationException("Request body is invalid.", "body");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("question", out var question) || question.ValueKind != JsonValueKind.String)
            {
                throw new BodyValidationException("Field required", "question");
            }

            var text = question.GetString() ?? "";
            if (text.Length > 500)
            {
                throw new BodyValidationException("String should have at most 500 characters", "question");
            }

            if (text.Trim().Length == 0)
            {
                throw new BodyValidationException("Question cannot be empty.", "question");
            }

            return text;
        }
    }
}
