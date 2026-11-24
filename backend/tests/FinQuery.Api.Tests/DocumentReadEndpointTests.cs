using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinQuery.Domain.Companies;
using FinQuery.Domain.Documents;
using FinQuery.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinQuery.Api.Tests;

public class DocumentReadEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly FinQueryApiFactory _factory;
    private readonly HttpClient _client;

    public DocumentReadEndpointTests(FinQueryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task List_IsNewestFirst_AndPrefersDisplayName()
    {
        var userId = await Authenticate("docs-list@example.com");
        var companyId = await CreateCompany("Listed Holdings Inc.", "LIST");
        var older = await AddDocument(userId, companyId, "Older Filing", new DateOnly(2020, 1, 1), "10-K", 2020);
        var newer = await AddDocument(userId, companyId, "Annual Report 2024", new DateOnly(2024, 3, 1), "10-K", 2024);
        await AddDocument(userId, null, "Unassigned Filing", null, "Other", null);

        var response = await _client.GetAsync("/api/v1/documents");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Unassigned Filing", rows[0].GetProperty("title").GetString());
        Assert.Equal("Annual Report 2024", rows[1].GetProperty("title").GetString());
        Assert.Equal("Older Filing", rows[2].GetProperty("title").GetString());
        Assert.Equal(newer, rows[1].GetProperty("id").GetInt32());
        Assert.Equal(older, rows[2].GetProperty("id").GetInt32());
        var annual = rows.Single(row => row.GetProperty("title").GetString() == "Annual Report 2024");
        Assert.Equal("LIST", annual.GetProperty("company_name").GetString());
        Assert.Equal("10-K", annual.GetProperty("document_type").GetString());
        Assert.Equal("2024-03-01", annual.GetProperty("filing_date").GetString());
        Assert.Equal(
            ["id", "company_id", "company_name", "title", "document_type", "filing_date", "processing_status", "created_at"],
            annual.EnumerateObject().Select(property => property.Name).ToArray());
        var unassigned = rows.Single(row => row.GetProperty("title").GetString() == "Unassigned Filing");
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("company_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("company_name").ValueKind);
    }

    [Fact]
    public async Task EmptyDisplayName_FallsBackToLegalName()
    {
        var userId = await Authenticate("docs-fallback@example.com");
        var companyId = await CreateCompany("Bare Name Inc.", "BARE");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
            var company = await db.Companies.FindAsync(companyId);
            company!.DisplayName = "";
            await db.SaveChangesAsync();
        }

        await AddDocument(userId, companyId, "Bare Filing", null, "10-K", null);
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/documents");
        Assert.Equal("Bare Name Inc.", body[0].GetProperty("company_name").GetString());
    }

    [Fact]
    public async Task Get_ReturnsTheDocument_AndHidesOtherUsers()
    {
        var userId = await Authenticate("docs-get@example.com");
        var companyId = await CreateCompany("Get Co", "GETC");
        var documentId = await AddDocument(userId, companyId, "FY2024 Annual Report", new DateOnly(2024, 12, 31), "10-K", 2024);

        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}");
        Assert.Equal("FY2024 Annual Report", fetched.GetProperty("title").GetString());
        Assert.Equal(2024, fetched.GetProperty("fiscal_year").GetInt32());
        Assert.Equal("en", fetched.GetProperty("language").GetString());
        Assert.False(fetched.TryGetProperty("user_id", out _));
        Assert.False(fetched.TryGetProperty("company_name", out _));

        await Authenticate("docs-get-other@example.com");
        var hidden = await _client.GetAsync($"/api/v1/documents/{documentId}");
        var hiddenBody = await hidden.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal($"Document with id {documentId} not found", hiddenBody.GetProperty("detail").GetString());
        Assert.Equal("NotFoundError", hiddenBody.GetProperty("error").GetString());
        Assert.Equal(0, (await _client.GetFromJsonAsync<JsonElement>("/api/v1/documents")).GetArrayLength());
    }

    [Fact]
    public async Task CompanyDocuments_FilterAndOrder_AndMissingCompanyIs404()
    {
        var userId = await Authenticate("docs-company@example.com");
        var companyId = await CreateCompany("Filter Co", "FLTR");
        var early = await AddDocument(userId, companyId, "Early", new DateOnly(2024, 1, 1), "10-K", 2024);
        var late = await AddDocument(userId, companyId, "Late", new DateOnly(2024, 6, 1), "10-Q", 2024);
        var undated = await AddDocument(userId, companyId, "Undated", null, "10-K", 2023);

        var all = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/companies/{companyId}/documents");
        Assert.Equal([late, early, undated], all.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());

        var onlyAnnual = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/companies/{companyId}/documents?document_type=10-K&fiscal_year=2024");
        Assert.Equal([early], onlyAnnual.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());

        var missing = await _client.GetAsync("/api/v1/companies/987654/documents");
        var missingBody = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Company with id 987654 not found", missingBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task InvalidBounds_Are422_AndMissingToken_Is401()
    {
        await Authenticate("docs-bounds@example.com");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.GetAsync("/api/v1/documents?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.GetAsync("/api/v1/companies/1/documents?fiscal_year=1800")).StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/documents");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Missing authentication token", body.GetProperty("detail").GetString());
    }

    private async Task<int> Authenticate(string email)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var userId = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return userId;
    }

    private async Task<int> CreateCompany(string legalName, string displayName)
    {
        var created = await _client.PostAsJsonAsync("/api/v1/companies", new { legal_name = legalName, display_name = displayName, ticker = displayName });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<int> AddDocument(
        int userId,
        int? companyId,
        string title,
        DateOnly? filingDate,
        string documentType,
        int? fiscalYear)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
        var now = DateTime.UtcNow;
        var document = new Document
        {
            UserId = userId,
            CompanyId = companyId,
            DocumentType = documentType,
            Title = title,
            FilingDate = filingDate,
            FiscalYear = fiscalYear,
            Language = "en",
            ProcessingStatus = "completed",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return document.Id;
    }
}
