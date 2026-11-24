using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinQuery.Api.Tests;

public class CompanyEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly HttpClient _client;

    public CompanyEndpointTests(FinQueryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateAndGet_NormalizesTickerAndCountry()
    {
        await Authenticate("companies-owner@example.com");
        var created = await Create(new
        {
            legal_name = "Tstc Industries Inc.",
            display_name = "TSTC",
            ticker = "tstc",
            exchange = "NASDAQ",
            country = "us"
        });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("TSTC", body.GetProperty("ticker").GetString());
        Assert.Equal("TSTC", body.GetProperty("display_name").GetString());
        Assert.Equal("US", body.GetProperty("country").GetString());
        Assert.Equal("NASDAQ", body.GetProperty("exchange").GetString());
        Assert.True(body.TryGetProperty("created_at", out _));

        var fetched = await _client.GetAsync($"/api/v1/companies/{body.GetProperty("id").GetInt32()}");
        var fetchedBody = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("Tstc Industries Inc.", fetchedBody.GetProperty("legal_name").GetString());
    }

    [Fact]
    public async Task MissingCompany_Is404()
    {
        await Authenticate("missing-company@example.com");
        var response = await _client.GetAsync("/api/v1/companies/987654");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Company with id 987654 not found", body.GetProperty("detail").GetString());
        Assert.Equal("NotFoundError", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DuplicateTickerAndLegalName_Are409()
    {
        await Authenticate("dup-company@example.com");
        Assert.Equal(HttpStatusCode.Created, (await Create(new { legal_name = "Dup Holdings", ticker = "DUPL", exchange = "NASDAQ" })).StatusCode);

        var sameTicker = await Create(new { legal_name = "Other Holdings", ticker = "dupl", exchange = "nasdaq" });
        var tickerBody = await sameTicker.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, sameTicker.StatusCode);
        Assert.Contains("ticker 'DUPL'", tickerBody.GetProperty("detail").GetString());

        var otherExchange = await Create(new { legal_name = "Dup Holdings Japan", ticker = "DUPL", exchange = "TSE" });
        Assert.Equal(HttpStatusCode.Created, otherExchange.StatusCode);

        var sameName = await Create(new { legal_name = "dup holdings japan" });
        Assert.Equal(HttpStatusCode.Conflict, sameName.StatusCode);
    }

    [Fact]
    public async Task Search_IsScopedToTheCaller()
    {
        await Authenticate("search-a@example.com");
        Assert.Equal(HttpStatusCode.Created, (await Create(new { legal_name = "Searchable Systems Inc", ticker = "SRCH" })).StatusCode);
        var created = await Create(new { legal_name = "Alpha Holdings Inc", ticker = "ALPH" });
        var companyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var found = await _client.GetFromJsonAsync<JsonElement>("/api/v1/companies?q=searchable");
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        Assert.Equal("SRCH", found.GetProperty("items")[0].GetProperty("ticker").GetString());

        await Authenticate("search-b@example.com");
        var hidden = await _client.GetFromJsonAsync<JsonElement>("/api/v1/companies");
        Assert.Equal(0, hidden.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/companies/{companyId}")).StatusCode);
    }

    [Fact]
    public async Task InvalidCountryAndTicker_Are422()
    {
        await Authenticate("invalid-company@example.com");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(new { legal_name = "Bad Country Ltd", country = "USA" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(new { legal_name = "Bad Ticker Ltd", ticker = "NO SPACE" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.GetAsync("/api/v1/companies?limit=0")).StatusCode);
    }

    [Fact]
    public async Task Patch_UpdatesAllowedFields_AndRejectsLegalName()
    {
        await Authenticate("patch-company@example.com");
        var created = await (await Create(new { legal_name = "Patch Co", ticker = "PTCH", country = "US" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var patched = await _client.PatchAsJsonAsync($"/api/v1/companies/{id}", new { display_name = "Patched", country = "gb" });
        var body = await patched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("Patched", body.GetProperty("display_name").GetString());
        Assert.Equal("GB", body.GetProperty("country").GetString());
        Assert.Equal("PTCH", body.GetProperty("ticker").GetString());

        var rejected = await _client.PatchAsJsonAsync($"/api/v1/companies/{id}", new { legal_name = "Renamed" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_Is401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/companies");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Missing authentication token", body.GetProperty("detail").GetString());
    }

    private async Task Authenticate(string email)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private Task<HttpResponseMessage> Create(object body) =>
        _client.PostAsJsonAsync("/api/v1/companies", body);
}
