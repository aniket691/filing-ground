using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using FinQuery.Application.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
public sealed class DocumentUploadController(
    DocumentUploadService uploads,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpPost("api/v1/documents/upload")]
    [RequestSizeLimit(28 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 28 * 1024 * 1024)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        if (!Request.HasFormContentType)
        {
            throw new BodyValidationException("Request body must be multipart form data.", "body");
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            throw new BodyValidationException("Field required", "file");
        }

        int? companyId = null;
        if (form.TryGetValue("company_id", out var companyRaw) && !string.IsNullOrEmpty(companyRaw.ToString()))
        {
            if (!int.TryParse(companyRaw.ToString(), out var parsed))
            {
                throw new BodyValidationException("Input should be a valid integer", "company_id");
            }

            companyId = parsed;
        }

        var documentType = form.TryGetValue("document_type", out var typeRaw) && !string.IsNullOrEmpty(typeRaw.ToString())
            ? typeRaw.ToString()
            : "Other";
        string? sourceUrl = form.TryGetValue("source_url", out var urlRaw) ? urlRaw.ToString() : null;
        string? sourceName = form.TryGetValue("source_name", out var nameRaw) ? nameRaw.ToString() : null;

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var created = await uploads.UploadAsync(
            user.Id,
            file.FileName,
            buffer.ToArray(),
            companyId,
            documentType,
            string.IsNullOrEmpty(sourceUrl) ? null : sourceUrl,
            sourceName,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}
