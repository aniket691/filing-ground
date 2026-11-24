using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinQuery.Application.Auth;
using FinQuery.Infrastructure.Auth;
using FinQuery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace FinQuery.Api.Tests;

public class AuthEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly FinQueryApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests(FinQueryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_LowercasesEmail_AndOmitsThePassword()
    {
        var response = await Register("Casey@Example.com", Password);
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("casey@example.com", body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("is_active").GetBoolean());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Is409_IgnoringCase()
    {
        Assert.Equal(HttpStatusCode.Created, (await Register("dup@example.com", Password)).StatusCode);

        var again = await Register("DUP@example.com", Password);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("An account with this email already exists.", body.GetProperty("detail").GetString());
        Assert.Equal("ConflictError", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("not-an-email", Password)]
    [InlineData("short@example.com", "short")]
    [InlineData("empty@example.com", "")]
    public async Task Register_InvalidInput_Is422(string email, string password)
    {
        var response = await Register(email, password);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_PasswordOver72Characters_Is422()
    {
        var response = await Register("long@example.com", new string('a', 73));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_PasswordOver72Bytes_Is422()
    {
        var response = await Register("bytes@example.com", new string('é', 40));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Password exceeds the maximum allowed length.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Register_StoresABcryptHash()
    {
        var response = await Register("hashed@example.com", Password);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
        var user = await db.Users.SingleAsync(row => row.Email == "hashed@example.com");

        Assert.NotEqual(Password, user.HashedPassword);
        Assert.StartsWith("$2", user.HashedPassword);
        Assert.True(new BcryptPasswordHasher(4).Verify(Password, user.HashedPassword));
    }

    [Fact]
    public void Hasher_VerifiesA2bHash()
    {
        var hasher = new BcryptPasswordHasher(4);
        var salt = BCrypt.Net.BCrypt.GenerateSalt(4, 'b');
        var hash = BCrypt.Net.BCrypt.HashPassword(Password, salt);

        Assert.StartsWith("$2b$", hash);
        Assert.True(hasher.Verify(Password, hash));
        Assert.False(hasher.Verify("other-password", hash));
    }

    [Fact]
    public async Task Login_ReturnsABearerToken_WhoseSubjectIsTheUserId()
    {
        var registered = await (await Register("login@example.com", Password)).Content.ReadFromJsonAsync<JsonElement>();
        var response = await Login("LOGIN@example.com", Password);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("bearer", body.GetProperty("token_type").GetString());
        Assert.Equal("login@example.com", body.GetProperty("user").GetProperty("email").GetString());
        Assert.False(body.TryGetProperty("password", out _));

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var jwt = handler.ReadJwtToken(body.GetProperty("access_token").GetString());
        Assert.Equal(registered.GetProperty("id").GetInt32().ToString(), jwt.Claims.Single(claim => claim.Type == "sub").Value);
        Assert.Contains(jwt.Claims, claim => claim.Type == "exp" || jwt.ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ShareTheMessage()
    {
        Assert.Equal(HttpStatusCode.Created, (await Register("known@example.com", Password)).StatusCode);

        var unknown = await Login("nobody@example.com", Password);
        var wrong = await Login("known@example.com", "WrongPass123!");
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        var wrongBody = await wrong.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Invalid email or password.", unknownBody.GetProperty("detail").GetString());
        Assert.Equal(unknownBody.GetProperty("detail").GetString(), wrongBody.GetProperty("detail").GetString());
        Assert.False(unknownBody.TryGetProperty("access_token", out _));
        Assert.False(wrongBody.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task Login_InactiveUser_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Created, (await Register("inactive@example.com", Password)).StatusCode);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
            var user = await db.Users.SingleAsync(row => row.Email == "inactive@example.com");
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        var response = await Login("inactive@example.com", Password);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ProtectedRoute_RequiresABearerToken()
    {
        var missing = await _client.GetAsync("/__test/me");
        var missingBody = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("Missing authentication token", missingBody.GetProperty("detail").GetString());
        Assert.False(missingBody.TryGetProperty("error", out _));

        var registered = await (await Register("guard@example.com", Password)).Content.ReadFromJsonAsync<JsonElement>();
        var login = await (await Login("guard@example.com", Password)).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/me");
        request.Headers.Add("Authorization", "Bearer " + login.GetProperty("access_token").GetString());
        var ok = await _client.SendAsync(request);
        var me = await ok.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(registered.GetProperty("id").GetInt32(), me.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task ProtectedRoute_AcceptsATokenWithOnlySubAndExp()
    {
        var registered = await (await Register("external@example.com", Password)).Content.ReadFromJsonAsync<JsonElement>();
        var userId = registered.GetProperty("id").GetInt32();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-with-enough-length"));
        var jwt = new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        var token = handler.WriteToken(jwt);

        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/me");
        request.Headers.Add("Authorization", "Bearer " + token);
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(userId, body.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Login_WithoutSecret_Is422()
    {
        await using var factory = new MissingSecretFactory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "configured@example.com",
            password = Password
        })).StatusCode);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "configured@example.com",
            password = Password
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("Authentication is not configured. Set AUTH_SECRET_KEY.", body.GetProperty("detail").GetString());
    }

    private Task<HttpResponseMessage> Register(string email, string password) =>
        _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password });

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private sealed class MissingSecretFactory : FinQueryApiFactory
    {
        protected override AuthSettings Auth { get; } = new()
        {
            SecretKey = "",
            Algorithm = "HS256",
            AccessTokenExpireMinutes = 30,
            BcryptRounds = 4
        };
    }
}
