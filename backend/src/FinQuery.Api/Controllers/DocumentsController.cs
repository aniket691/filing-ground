using FinQuery.Api.Auth;
using FinQuery.Api.Companies;
using FinQuery.Application.Auth;
using FinQuery.Application.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
[Route("api/v1/documents")]
public sealed class DocumentsController(
    DocumentReadService documents,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var limit = CompanyRequestReader.ParseBound(Request.Query["limit"].FirstOrDefault(), 50, 1, 200, "limit");
        return Ok(await documents.ListAsync(user.Id, limit, cancellationToken));
    }

    [HttpGet("{documentId:int}")]
    public async Task<IActionResult> Get(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await documents.GetAsync(user.Id, documentId, cancellationToken));
    }

    [HttpGet("/api/v1/companies/{companyId:int}/documents")]
    public async Task<IActionResult> ListForCompany(int companyId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        int? fiscalYear = null;
        var rawYear = Request.Query["fiscal_year"].FirstOrDefault();
        if (!string.IsNullOrEmpty(rawYear))
        {
            if (!int.TryParse(rawYear, out var year) || year < 1900 || year > 2100)
            {
                throw new BodyValidationException("Input should be greater than or equal to 1900", "fiscal_year");
            }

            fiscalYear = year;
        }

        var limit = CompanyRequestReader.ParseBound(Request.Query["limit"].FirstOrDefault(), 50, 1, 200, "limit");
        var offset = CompanyRequestReader.ParseBound(Request.Query["offset"].FirstOrDefault(), 0, 0, int.MaxValue, "offset");
        var rows = await documents.ListForCompanyAsync(
            user.Id,
            companyId,
            Request.Query["document_type"].FirstOrDefault(),
            fiscalYear,
            limit,
            offset,
            cancellationToken);
        return Ok(rows);
    }
}
