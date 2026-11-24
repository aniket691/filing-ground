using System.Text.Json;
using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using FinQuery.Application.Companies;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Companies;

[ApiController]
[Route("api/v1/companies")]
public sealed class CompaniesController(
    CompanyService companies,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var query = Request.Query["q"].FirstOrDefault();
        if (query is not null && query.Length > 255)
        {
            throw new BodyValidationException("String should have at most 255 characters", "q");
        }

        var limit = CompanyRequestReader.ParseBound(Request.Query["limit"].FirstOrDefault(), 50, 1, 200, "limit");
        var offset = CompanyRequestReader.ParseBound(Request.Query["offset"].FirstOrDefault(), 0, 0, int.MaxValue, "offset");
        return Ok(await companies.ListAsync(user.Id, query, limit, offset, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var created = await companies.CreateAsync(user.Id, CompanyRequestReader.ReadCreate(body), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{companyId:int}")]
    public async Task<IActionResult> Get(int companyId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await companies.GetAsync(user.Id, companyId, cancellationToken));
    }

    [HttpPatch("{companyId:int}")]
    public async Task<IActionResult> Update(int companyId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await companies.UpdateAsync(user.Id, companyId, CompanyRequestReader.ReadPatch(body), cancellationToken));
    }
}
