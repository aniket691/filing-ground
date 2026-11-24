using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinQuery.Api.Tests;

public class LegacyRagEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private readonly HttpClient _client;

    public LegacyRagEndpointTests(FinQueryApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task DummyQuery_Waits_AndReturnsThePlaceholder()
    {
        var response = await _client.PostAsJsonAsync("/query", new { question = "What was revenue?" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("What was revenue?", body.GetProperty("question").GetString());
        Assert.Contains("dummy reply", body.GetProperty("answer").GetString());
        Assert.Equal("dummy", body.GetProperty("model").GetString());
        Assert.Equal("dummy://finquery", body.GetProperty("sources")[0].GetProperty("filename").GetString());
        Assert.True(body.GetProperty("processing_time_ms").GetDouble() >= 450);
    }

    [Fact]
    public async Task Question_Over500_Is422_AndBlank_Is422()
    {
        var longQuestion = new string('a', 501);
        var tooLong = await _client.PostAsJsonAsync("/query", new { question = longQuestion });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);

        var blank = await _client.PostAsJsonAsync("/query", new { question = "   " });
        var body = await blank.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blank.StatusCode);
        Assert.Contains("Question cannot be empty.", body.GetProperty("detail")[0].GetProperty("msg").GetString());
    }
}

public class LegacyRagModeTests : IClassFixture<RagHealthFactory>
{
    private readonly HttpClient _client;

    public LegacyRagModeTests(RagHealthFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task EmptyIndex_Is400_ThenIngestResetAndKeyFailures()
    {
        var missing = await _client.PostAsJsonAsync("/query", new { question = "revenue" });
        var missingBody = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("No documents ingested. Call POST /ingest first.", missingBody.GetProperty("detail").GetString());

        var path = Path.Combine(Path.GetTempPath(), "finquery-note-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "Total net sales were $10 million.");
        try
        {
            var ingested = await _client.PostAsJsonAsync("/ingest", new { file_paths = new[] { path } });
            var ingestedBody = await ingested.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.OK, ingested.StatusCode);
            Assert.Equal("success", ingestedBody.GetProperty("status").GetString());
            Assert.True(ingestedBody.GetProperty("chunks_added").GetInt32() >= 1);

            var documents = await _client.GetFromJsonAsync<JsonElement>("/documents");
            Assert.True(documents.GetProperty("total_documents").GetInt32() >= 1);
            Assert.Equal("financial_docs", documents.GetProperty("collection_name").GetString());
            Assert.Equal("Jina AI (cloud)", documents.GetProperty("embedding_api").GetString());

            var health = await _client.GetFromJsonAsync<JsonElement>("/health");
            Assert.True(health.GetProperty("pipeline_ready").GetBoolean());
            Assert.True(health.GetProperty("total_chunks").GetInt32() >= 1);

            var answer = await _client.PostAsJsonAsync("/query", new { question = "What was revenue?" });
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

            var expired = await _client.PostAsJsonAsync("/query", new { question = "expired_api_key" });
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
            Assert.Contains("GROQ_API_KEY", await expired.Content.ReadAsStringAsync());

            var down = await _client.PostAsJsonAsync("/query", new { question = "groq-down" });
            var downBody = await down.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.InternalServerError, down.StatusCode);
            Assert.Equal("model overloaded", downBody.GetProperty("detail").GetString());

            var reset = await _client.DeleteAsync("/reset");
            var resetBody = await reset.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            Assert.Equal("cleared", resetBody.GetProperty("status").GetString());
            var after = await _client.GetFromJsonAsync<JsonElement>("/documents");
            Assert.Equal(0, after.GetProperty("total_documents").GetInt32());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
