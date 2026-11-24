using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Metrics;
using FinQuery.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinQuery.Api.Tests;

public class DashboardStatsTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly FinQueryApiFactory _factory;
    private readonly HttpClient _client;

    public DashboardStatsTests(FinQueryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EmptyWorkspace_IsZeroOnBothPaths()
    {
        await Authenticate("stats-empty@example.com");

        var versioned = await _client.GetFromJsonAsync<JsonElement>("/api/v1/stats/dashboard");
        var alias = await _client.GetFromJsonAsync<JsonElement>("/stats/dashboard");

        Assert.Equal(0, versioned.GetProperty("documents").GetInt32());
        Assert.Equal(0, versioned.GetProperty("companies").GetInt32());
        Assert.Equal(0, versioned.GetProperty("financial_metrics").GetInt32());
        Assert.Equal(versioned.GetProperty("documents").GetInt32(), alias.GetProperty("documents").GetInt32());
        Assert.Equal(versioned.GetProperty("companies").GetInt32(), alias.GetProperty("companies").GetInt32());
        Assert.Equal(versioned.GetProperty("financial_metrics").GetInt32(), alias.GetProperty("financial_metrics").GetInt32());
    }

    [Fact]
    public async Task Counts_AreScopedThroughTheCaller()
    {
        var ownerId = await Authenticate("stats-owner@example.com");
        var created = await _client.PostAsJsonAsync("/api/v1/companies", new { legal_name = "Stats Co", ticker = "STAA" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var companyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await AddDocumentAndMetric(ownerId, companyId);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/stats/dashboard");
        Assert.Equal(1, body.GetProperty("documents").GetInt32());
        Assert.Equal(1, body.GetProperty("companies").GetInt32());
        Assert.Equal(1, body.GetProperty("financial_metrics").GetInt32());

        await Authenticate("stats-other@example.com");
        var hidden = await _client.GetFromJsonAsync<JsonElement>("/api/v1/stats/dashboard");
        Assert.Equal(0, hidden.GetProperty("documents").GetInt32());
        Assert.Equal(0, hidden.GetProperty("companies").GetInt32());
        Assert.Equal(0, hidden.GetProperty("financial_metrics").GetInt32());
    }

    [Fact]
    public async Task MissingToken_Is401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/stats/dashboard");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Missing authentication token", body.GetProperty("detail").GetString());
        Assert.False(body.TryGetProperty("error", out _));
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

    private async Task AddDocumentAndMetric(int userId, int companyId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
        var now = DateTime.UtcNow;
        var document = new Document
        {
            UserId = userId,
            CompanyId = companyId,
            DocumentType = "10-K",
            Title = "Stats filing",
            Language = "en",
            ProcessingStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        db.FinancialMetrics.Add(new FinancialMetric
        {
            CompanyId = companyId,
            DocumentId = document.Id,
            MetricName = "Revenue",
            NormalizedMetricName = "revenue",
            Value = 123m,
            Unit = "units",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
    }
}
