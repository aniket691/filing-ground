using System.Text.Json;
using FinQuery.Api.Auth;
using FinQuery.Api.Companies;
using FinQuery.Api.Documents;
using FinQuery.Application.Auth;
using FinQuery.Application.Documents;
using Microsoft.AspNetCore.Mvc;

namespace FinQuery.Api.Controllers;

[ApiController]
[Route("api/v1/documents")]
public sealed class DocumentGraphController(
    DocumentGraphService graph,
    IAccessTokenReader tokens,
    AuthService auth) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var document = GraphRequestReader.ReadDocument(body);
        if (document.Language.Length < 2)
        {
            throw new BodyValidationException("String should have at least 2 characters", "language");
        }

        var created = await graph.CreateAsync(user.Id, document, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{documentId:int}/pages")]
    public async Task<IActionResult> ListPages(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await graph.ListPagesAsync(user.Id, documentId, cancellationToken));
    }

    [HttpPost("{documentId:int}/pages")]
    public async Task<IActionResult> AddPage(int documentId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var created = await graph.AddPageAsync(user.Id, documentId, GraphRequestReader.ReadPage(body), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{documentId:int}/sections")]
    public async Task<IActionResult> ListSections(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await graph.ListSectionsAsync(user.Id, documentId, cancellationToken));
    }

    [HttpPost("{documentId:int}/sections")]
    public async Task<IActionResult> AddSection(int documentId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var created = await graph.AddSectionAsync(user.Id, documentId, GraphRequestReader.ReadSection(body), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{documentId:int}/chunks")]
    public async Task<IActionResult> ListChunks(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var limit = CompanyRequestReader.ParseBound(Request.Query["limit"].FirstOrDefault(), 100, 1, 500, "limit");
        var offset = CompanyRequestReader.ParseBound(Request.Query["offset"].FirstOrDefault(), 0, 0, int.MaxValue, "offset");
        return Ok(await graph.ListChunksAsync(user.Id, documentId, Request.Query["chunk_type"].FirstOrDefault(), limit, offset, cancellationToken));
    }

    [HttpPost("{documentId:int}/chunks")]
    public async Task<IActionResult> AddChunk(int documentId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var created = await graph.AddChunkAsync(user.Id, documentId, GraphRequestReader.ReadChunk(body), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{documentId:int}/tables")]
    public async Task<IActionResult> ListTables(int documentId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await graph.ListTablesAsync(user.Id, documentId, cancellationToken));
    }

    [HttpPost("{documentId:int}/tables")]
    public async Task<IActionResult> AddTable(int documentId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        var created = await graph.AddTableAsync(user.Id, documentId, GraphRequestReader.ReadTable(body), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{documentId:int}/tables/{tableId:long}")]
    public async Task<IActionResult> GetTable(int documentId, long tableId, CancellationToken cancellationToken)
    {
        var user = await CurrentUser.RequireAsync(Request, tokens, auth, cancellationToken);
        return Ok(await graph.GetTableAsync(user.Id, documentId, tableId, cancellationToken));
    }
}
