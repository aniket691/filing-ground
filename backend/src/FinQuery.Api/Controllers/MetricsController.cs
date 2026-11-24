using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinQuery.Api.Auth;
using FinQuery.Api.Companies;
using FinQuery.Api.Documents;
using FinQuery.Application.Auth;
using FinQuery.Application.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
[Route("api/v1/companies")]
public sealed class MetricsController(
    DocumentGraphService graph,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    private static readonly Regex Quarter = new("^Q[1-4]$", RegexOptions.CultureInvariant);

    [HttpPost("{companyId:int}/metrics")]
    public async Task<IActionResult> Create(int companyId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var created = await graph.CreateMetricAsync(user.Id, companyId, GraphRequestReader.ReadMetric(body), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{companyId:int}/metrics")]
    public async Task<IActionResult> List(int companyId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var name = Request.Query["metric_name"].FirstOrDefault();
        if (name is not null && name.Length > 255)
        {
            throw new BodyValidationException("String should have at most 255 characters", "metric_name");
        }

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

        var quarter = Request.Query["fiscal_quarter"].FirstOrDefault();
        if (!string.IsNullOrEmpty(quarter) && !Quarter.IsMatch(quarter))
        {
            throw new BodyValidationException("String should match pattern '^Q[1-4]$'", "fiscal_quarter");
        }

        var limit = CompanyRequestReader.ParseBound(Request.Query["limit"].FirstOrDefault(), 100, 1, 500, "limit");
        var offset = CompanyRequestReader.ParseBound(Request.Query["offset"].FirstOrDefault(), 0, 0, int.MaxValue, "offset");
        var rows = await graph.ListMetricsAsync(
            user.Id,
            companyId,
            name,
            fiscalYear,
            quarter,
            ParseDate("period_start"),
            ParseDate("period_end"),
            limit,
            offset,
            cancellationToken);
        return Ok(rows);
    }

    [HttpGet("{companyId:int}/metrics/{metricId:long}")]
    public async Task<IActionResult> Get(int companyId, long metricId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await graph.GetMetricAsync(user.Id, companyId, metricId, cancellationToken));
    }

    private DateOnly? ParseDate(string name)
    {
        var raw = Request.Query[name].FirstOrDefault();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new BodyValidationException("Input should be a valid date", name);
        }

        return date;
    }
}
