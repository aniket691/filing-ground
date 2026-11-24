using System.Text.Json;
using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using FinQuery.Application.Rag;
using FinQuery.Domain.Errors;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
[Route("api/v1/rag")]
public sealed class RagController(
    ProductRagService rag,
    IProductEmbedder embedder,
    IUserDocumentIds documents,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpPost("query")]
    public async Task<IActionResult> Query(CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var question = await ReadQuestionAsync(cancellationToken);
        try
        {
            _ = embedder.ModelName;
            embedder.Embed([]);
        }
        catch (Exception exception)
        {
            throw new ValidationError(exception.Message);
        }

        var ids = await documents.ListAsync(user.Id, cancellationToken);
        return Ok(rag.Answer(question, ids));
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
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("question", out var question)
                || question.ValueKind != JsonValueKind.String)
            {
                throw new BodyValidationException("Field required", "question");
            }

            var text = question.GetString() ?? "";
            if (text.Length > 4000)
            {
                throw new BodyValidationException("String should have at most 4000 characters", "question");
            }

            if (text.Length < 1)
            {
                throw new BodyValidationException("String should have at least 1 character", "question");
            }

            return text;
        }
    }
}
