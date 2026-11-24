using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinQuery.Application.Rag;

namespace FinQuery.Api.Tests;

public class ProductRagEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly FinQueryApiFactory _factory;
    private readonly HttpClient _client;

    public ProductRagEndpointTests(FinQueryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Query_UsesIndexedChunks_AndHidesOtherUsers()
    {
        await Authenticate("rag-owner@example.com");
        var companyId = await CreateCompany("Rag Co", "RAGC");
        var documentId = await Upload(companyId, "facts.txt", "Total net sales were $10 million."u8.ToArray());
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"/api/v1/documents/{documentId}/process", null)).StatusCode);
        var status = await Wait(documentId);
        Assert.Equal("processed", status.GetProperty("status").GetString());

        var answer = await _client.PostAsJsonAsync("/api/v1/rag/query", new { question = "What was revenue?" });
        var body = await answer.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("Revenue was stated in the passage.", body.GetProperty("answer").GetString());
        Assert.Equal(documentId, body.GetProperty("sources")[0].GetProperty("document_id").GetInt32());
        Assert.True(body.GetProperty("sources")[0].GetProperty("score").GetDouble() > 0);

        var failed = await _client.PostAsJsonAsync("/api/v1/rag/query", new { question = "llm-fail please" });
        var failedBody = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal("I don't have enough information in the provided documents.", failedBody.GetProperty("answer").GetString());
        Assert.True(failedBody.GetProperty("sources").GetArrayLength() >= 1);

        var other = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/v1/auth/register", new { email = "rag-other@example.com", password = Password })).StatusCode);
        var login = await other.PostAsJsonAsync("/api/v1/auth/login", new { email = "rag-other@example.com", password = Password });
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        var hidden = await other.PostAsJsonAsync("/api/v1/rag/query", new { question = "What was revenue?" });
        var hiddenBody = await hidden.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.Equal("I don't have enough information in the provided documents.", hiddenBody.GetProperty("answer").GetString());
        Assert.Equal(0, hiddenBody.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public async Task Query_Validation_AndMissingToken()
    {
        var anonymous = _factory.CreateClient();
        var missing = await anonymous.PostAsJsonAsync("/api/v1/rag/query", new { question = "hello" });
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        await Authenticate("rag-validate@example.com");
        var blank = await _client.PostAsJsonAsync("/api/v1/rag/query", new { question = "   " });
        var blankBody = await blank.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Equal("Question must not be empty", blankBody.GetProperty("detail").GetString());
        Assert.Equal("ValidationError", blankBody.GetProperty("error").GetString());

        var tooLong = await _client.PostAsJsonAsync("/api/v1/rag/query", new { question = new string('q', 4001) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
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

    private async Task<JsonElement> Wait(int documentId)
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
}
