using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinQuery.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinQuery.Api.Tests;

public class IngestionEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly FinQueryApiFactory _factory;
    private readonly HttpClient _client;

    public IngestionEndpointTests(FinQueryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Process_LoadsText_AndStatusBecomesProcessed()
    {
        await Authenticate("ingest-text@example.com");
        var companyId = await CreateCompany("Ingest Co", "INGT");
        var documentId = await Upload(companyId, "note.txt", "Revenue grew.\n12\n"u8.ToArray());

        var before = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/status");
        Assert.Equal("uploaded", before.GetProperty("status").GetString());

        var queued = await _client.PostAsync($"/api/v1/documents/{documentId}/process", null);
        var queuedBody = await queued.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        Assert.Equal(documentId, queuedBody.GetProperty("document_id").GetInt32());
        Assert.Equal("queued", queuedBody.GetProperty("status").GetString());

        var after = await WaitForTerminal(documentId);
        Assert.Equal("processed", after.GetProperty("status").GetString());
        Assert.Equal(1, after.GetProperty("page_count").GetInt32());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("error").ValueKind);

        var pages = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/pages");
        Assert.Equal("Revenue grew.", pages[0].GetProperty("cleaned_text").GetString());
        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/v1/ingestion/stats");
        Assert.True(stats.GetProperty("documents_processed").GetInt32() >= 1);
        Assert.True(stats.TryGetProperty("uptime_seconds", out _));
    }

    [Fact]
    public async Task Process_PersistsSectionsTablesAndMetrics()
    {
        await Authenticate("ingest-structure@example.com");
        var companyId = await CreateCompany("Structured Co", "STRC");
        var body = """
            Item 1. Business

            The company sells software.

            Income Statement

            Revenue $10 million in fiscal year 2024.

            Annual results
            | Item | 2023 | 2024 |
            | Revenue | 10 | 12 |
            | Costs | 4 | 5 |
            | Profit | 6 | 7 |
            """;
        var documentId = await Upload(companyId, "filing.txt", System.Text.Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"/api/v1/documents/{documentId}/process", null)).StatusCode);

        var status = await WaitForTerminal(documentId);
        Assert.Equal("processed", status.GetProperty("status").GetString());

        var sections = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/sections");
        Assert.Contains(sections.EnumerateArray(), section => section.GetProperty("section_title").GetString() == "Business Overview");
        Assert.Contains(sections.EnumerateArray(), section => section.GetProperty("section_title").GetString() == "Income Statement");

        var tables = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/tables");
        Assert.Equal(1, tables.GetArrayLength());
        Assert.Equal("Annual results", tables[0].GetProperty("title").GetString());
        Assert.True(tables[0].GetProperty("rows").GetArrayLength() >= 2);

        var metrics = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/companies/{companyId}/metrics");
        Assert.Contains(metrics.EnumerateArray(), metric =>
            metric.GetProperty("normalized_metric_name").GetString() == "revenue"
            && metric.GetProperty("value").GetDecimal() == 10_000_000m);
    }

    [Fact]
    public async Task UnknownDocument_Is404_AndProcessing_Is409()
    {
        await Authenticate("ingest-missing@example.com");
        var missing = await _client.PostAsync("/api/v1/documents/999999/process", null);
        var body = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Document with id 999999 not found", body.GetProperty("detail").GetString());

        var companyId = await CreateCompany("Busy Co", "BUSY");
        var documentId = await Upload(companyId, "busy.txt", "hello"u8.ToArray());
        await SetStatus(documentId, "processing");
        var conflict = await _client.PostAsync($"/api/v1/documents/{documentId}/process", null);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("already being processed", conflictBody);
    }

    [Fact]
    public async Task Stats_WithoutToken_Is401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/ingestion/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task SetStatus(int documentId, string status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
        var document = await db.Documents.FindAsync(documentId);
        document!.ProcessingStatus = status;
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> WaitForTerminal(int documentId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        JsonElement last = default;
        while (DateTime.UtcNow < deadline)
        {
            last = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{documentId}/status");
            var status = last.GetProperty("status").GetString();
            if (status is "processed" or "partially_processed" or "failed")
            {
                return last;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Ingestion did not finish. Last status was {last}.");
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

    private async Task<int> Upload(int companyId, string filename, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", filename);
        form.Add(new StringContent(companyId.ToString()), "company_id");
        var response = await _client.PostAsync("/api/v1/documents/upload", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
}
