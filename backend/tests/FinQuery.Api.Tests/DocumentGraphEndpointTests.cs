using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinQuery.Api.Tests;

public class DocumentGraphEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly HttpClient _client;

    public DocumentGraphEndpointTests(FinQueryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateDocument_DuplicateHash_Is409()
    {
        await Authenticate("graph-hash@example.com");
        var companyId = await CreateCompany("Hash Co", "HASH");
        var first = await CreateDocument(companyId, new { file_hash = "cafe1234" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await CreateDocument(companyId, new { file_hash = "cafe1234" });
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("file_hash 'cafe1234'", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnsupportedDocumentType_AndBadPeriod_Are422()
    {
        await Authenticate("graph-type@example.com");
        var companyId = await CreateCompany("Bad Type", "BADD");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreateDocument(companyId, new { document_type = "Form 999-X" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreateDocument(companyId, new { reporting_period_start = "2024-12-31", reporting_period_end = "2024-01-01" })).StatusCode);
    }

    [Fact]
    public async Task PagesSectionsAndChunks_FollowOwnershipRules()
    {
        await Authenticate("graph-pages@example.com");
        var companyId = await CreateCompany("Pages Co", "PAGS");
        var documentId = await DocumentId(await CreateDocument(companyId, null));

        var page = await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/pages", new { page_number = 1, raw_text = "Page one text", extraction_method = "text" });
        Assert.Equal(HttpStatusCode.Created, page.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/pages", new { page_number = 1, raw_text = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/pages", new { page_number = 0, raw_text = "zero" })).StatusCode);
        var listed = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/pages");
        Assert.Equal(1, listed[0].GetProperty("page_number").GetInt32());

        var parent = await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/sections", new { section_title = "Risk Factors", section_type = "Risk Factors", page_start = 5, page_end = 12 });
        var parentId = (await parent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var child = await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/sections", new { section_title = "Supply Chain Risk", section_type = "Risk Factors", parent_section_id = parentId });
        Assert.Equal(HttpStatusCode.Created, child.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/sections", new { section_title = "Orphan", section_type = "Other", parent_section_id = 987654 })).StatusCode);
        Assert.Equal(2, (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/sections")).GetArrayLength());

        var chunk = await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/chunks", new { chunk_index = 0, text = "Total revenue grew 8%.", token_count = 6 });
        var chunkBody = await chunk.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, chunk.StatusCode);
        Assert.Equal(JsonValueKind.Object, chunkBody.GetProperty("chunk_metadata").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/chunks", new { chunk_index = 0, text = "duplicate index" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/chunks", new { chunk_index = 1, text = "x", chunk_type = "video" })).StatusCode);
    }

    [Fact]
    public async Task Metrics_NormalizeName_AndRejectBadProvenance()
    {
        await Authenticate("graph-metrics@example.com");
        var companyId = await CreateCompany("Metric Co", "MTRX");
        var otherCompany = await CreateCompany("Other Co", "CMPB");
        var documentId = await DocumentId(await CreateDocument(companyId, null));
        var chunk = await (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/chunks", new { chunk_index = 0, text = "Net sales increased." })).Content.ReadFromJsonAsync<JsonElement>();

        var metric = await _client.PostAsJsonAsync($"/api/v1/companies/{companyId}/metrics", new
        {
            document_id = documentId,
            metric_name = "Revenue",
            value = "391035000000.00",
            unit = "USD",
            currency = "USD",
            fiscal_year = 2024,
            metric_type = "income_statement",
            source_page = 25,
            source_chunk_id = chunk.GetProperty("id").GetInt64(),
            confidence = 0.99
        });
        var body = await metric.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, metric.StatusCode);
        Assert.Equal("revenue", body.GetProperty("normalized_metric_name").GetString());

        var listed = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/companies/{companyId}/metrics?metric_name=revenue&fiscal_year=2024");
        Assert.Equal(body.GetProperty("id").GetInt64(), listed[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/v1/companies/{companyId}/metrics/{body.GetProperty("id").GetInt64()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/v1/companies/999999/metrics")).StatusCode);

        var missingProvenance = await _client.PostAsJsonAsync($"/api/v1/companies/{companyId}/metrics", new { document_id = documentId, metric_name = "Revenue", value = "100" });
        var provenanceBody = await missingProvenance.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingProvenance.StatusCode);
        Assert.Contains("provenance", provenanceBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync($"/api/v1/companies/{companyId}/metrics", new { document_id = documentId, metric_name = "Revenue", value = "NaN", source_page = 1 })).StatusCode);

        var cross = await _client.PostAsJsonAsync($"/api/v1/companies/{otherCompany}/metrics", new { document_id = documentId, metric_name = "Revenue", value = "1", source_page = 3 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cross.StatusCode);
        Assert.Contains("does not belong", (await cross.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Table_RoundTrip_PreservesRows()
    {
        await Authenticate("graph-tables@example.com");
        var companyId = await CreateCompany("Table Co", "TBLS");
        var documentId = await DocumentId(await CreateDocument(companyId, null));
        var created = await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/tables", new
        {
            title = "Revenue by Geography",
            page_number = 30,
            headers = new[] { "Region", "2024", "2025" },
            rows = new object[]
            {
                new { row_index = 0, row_label = "Americas", cells = new[] { "Americas", "100B", "110B" } },
                new { row_index = 1, row_label = "Europe", cells = new[] { "Europe", "50B", "55B" } },
                new { row_index = 2, row_label = "Asia", cells = new[] { "Asia", "30B", "32B" } }
            },
            extraction_confidence = 0.95
        });
        var table = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(3, table.GetProperty("rows").GetArrayLength());
        Assert.Equal("Region", table.GetProperty("headers")[0].GetString());

        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/tables/{table.GetProperty("id").GetInt64()}");
        Assert.Equal(["Americas", "Europe", "Asia"], fetched.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("row_label").GetString()).ToArray());
        Assert.Equal("55B", fetched.GetProperty("rows")[1].GetProperty("cells")[2].GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsJsonAsync($"/api/v1/documents/{documentId}/tables", new { title = "Empty" })).StatusCode);
    }

    private async Task Authenticate(string email)
    {
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password })).StatusCode);
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<int> CreateCompany(string legalName, string ticker)
    {
        var created = await _client.PostAsJsonAsync("/api/v1/companies", new { legal_name = legalName, ticker });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> CreateDocument(int companyId, object? extra)
    {
        var payload = new Dictionary<string, object?>
        {
            ["company_id"] = companyId,
            ["document_type"] = "10-K",
            ["title"] = "FY2024 Annual Report",
            ["fiscal_year"] = 2024,
            ["currency"] = "USD"
        };
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties())
            {
                payload[property.Name] = property.GetValue(extra);
            }
        }

        return _client.PostAsJsonAsync("/api/v1/documents", payload);
    }

    private static async Task<int> DocumentId(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
}
