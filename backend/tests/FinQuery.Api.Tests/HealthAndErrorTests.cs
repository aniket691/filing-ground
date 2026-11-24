using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinQuery.Api.Configuration;
using FinQuery.Application.Auth;
using FinQuery.Application.Health;
using FinQuery.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FinQuery.Api.Tests;

public class FinQueryApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public FinQueryApiFactory()
    {
        _connection.Open();
    }

    protected virtual FinQuerySettings Settings { get; } = new()
    {
        ChatMode = "dummy",
        LlmModel = "openai/gpt-oss-120b",
        GroqApiKey = "",
        JinaApiKey = "",
        CorsOrigins = ["http://localhost:5173"]
    };

    protected virtual AuthSettings Auth { get; } = new()
    {
        SecretKey = "test-secret-key-with-enough-length",
        Algorithm = "HS256",
        AccessTokenExpireMinutes = 30,
        BcryptRounds = 4
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Program binds settings before test configuration exists.
            // Replace the singletons so the host reads the values under test.
            foreach (var descriptor in services.Where(d =>
                d.ServiceType == typeof(FinQuerySettings) || d.ServiceType == typeof(AuthSettings)).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddSingleton(Settings);
            services.AddSingleton(Auth);
            foreach (var descriptor in services.Where(d =>
                d.ServiceType == typeof(FinQueryDbContext)
                || d.ServiceType == typeof(DbContextOptions<FinQueryDbContext>)
                || d.ServiceType == typeof(DbContextOptions)).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<FinQueryDbContext>(options =>
                options.UseSqlite(_connection).AddInterceptors(SqliteCommandGate.Instance));
            services.AddSingleton<IStartupFilter, TestUsersSchemaStartup>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Creates the users table on the throwaway SQLite connection only.
/// Production startup never calls this.
/// </summary>
file sealed class SqliteCommandGate : DbCommandInterceptor
{
    public static readonly SqliteCommandGate Instance = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbConnection, object> Gates = new();

    private static object Gate(DbCommand command) => Gates.GetValue(command.Connection!, static connection => new object());

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Monitor.Enter(Gate(command));
        return result;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Exit(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Monitor.Enter(Gate(command));
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Exit(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Monitor.Enter(Gate(command));
        return result;
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Exit(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Monitor.Enter(Gate(command));
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Exit(command);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Exit(command);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Exit(command);
        return Task.CompletedTask;
    }

    private static void Exit(DbCommand command)
    {
        var gate = Gate(command);
        if (Monitor.IsEntered(gate))
        {
            Monitor.Exit(gate);
        }
    }
}

file sealed class TestUsersSchemaStartup : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        using var scope = app.ApplicationServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinQueryDbContext>();
        db.Database.EnsureCreated();
        next(app);
    };
}

public class HealthEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(FinQueryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_ReturnsAlive()
    {
        var response = await _client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("alive", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_UsesSnakeCase_AndDummyRules()
    {
        var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"pipeline_ready\"", json);
        Assert.DoesNotContain("pipelineReady", json);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("pipeline_ready").GetBoolean());
        Assert.Equal(0, body.GetProperty("total_chunks").GetInt32());
        Assert.Equal("dummy", body.GetProperty("llm_model").GetString());
        Assert.False(body.GetProperty("groq_configured").GetBoolean());
        Assert.False(body.GetProperty("jina_configured").GetBoolean());
        Assert.Equal("dummy", body.GetProperty("chat_mode").GetString());
    }

    [Fact]
    public async Task AllowedOrigin_ReceivesCorsHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}

public class RagHealthEndpointTests : IClassFixture<RagHealthFactory>
{
    private readonly HttpClient _client;

    public RagHealthEndpointTests(RagHealthFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RagMode_IsNotReady_UntilChromaIsWired()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("pipeline_ready").GetBoolean());
        Assert.Equal(0, body.GetProperty("total_chunks").GetInt32());
        Assert.Equal("openai/gpt-oss-120b", body.GetProperty("llm_model").GetString());
        Assert.True(body.GetProperty("groq_configured").GetBoolean());
        Assert.Equal("rag", body.GetProperty("chat_mode").GetString());
    }
}

public sealed class RagHealthFactory : FinQueryApiFactory
{
    protected override FinQuerySettings Settings { get; } = new()
    {
        ChatMode = "rag",
        LlmModel = "openai/gpt-oss-120b",
        GroqApiKey = "test-key",
        JinaApiKey = "",
        CorsOrigins = ["http://localhost:5173"]
    };
}

public class SettingsBinderTests
{
    [Fact]
    public void Bind_ReadsPythonEnvironmentNames()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CHAT_MODE"] = "rag",
                ["LLM_MODEL"] = "openai/gpt-oss-120b",
                ["GROQ_API_KEY"] = "g",
                ["JINA_API_KEY"] = "",
                ["BACKEND_CORS_ORIGINS"] = "http://localhost:5173, http://localhost:8080"
            })
            .Build();

        var settings = FinQuerySettingsBinder.Bind(configuration);

        Assert.Equal("rag", settings.ChatMode);
        Assert.Equal("openai/gpt-oss-120b", settings.LlmModel);
        Assert.Equal("g", settings.GroqApiKey);
        Assert.Equal("", settings.JinaApiKey);
        Assert.Equal(["http://localhost:5173", "http://localhost:8080"], settings.CorsOrigins);
    }
}

public class ErrorMiddlewareTests : IClassFixture<FinQueryApiFactory>
{
    private readonly HttpClient _client;

    public ErrorMiddlewareTests(FinQueryApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NotFound_UsesFastApiShape()
    {
        var response = await _client.GetAsync("/__test/error?kind=not-found");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Document with id 9 not found", body.GetProperty("detail").GetString());
        Assert.Equal("NotFoundError", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    [InlineData("validation", HttpStatusCode.UnprocessableEntity)]
    [InlineData("auth", HttpStatusCode.Unauthorized)]
    public async Task DomainErrors_MapToPythonStatusCodes(string kind, HttpStatusCode status)
    {
        var response = await _client.GetAsync($"/__test/error?kind={kind}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(status, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task UnexpectedException_Is500_WithoutTheExceptionText()
    {
        var response = await _client.GetAsync("/__test/error?kind=boom");
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Internal Server Error", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("boom", json);
        Assert.False(body.TryGetProperty("error", out _));
    }
}

public class SwaggerDevelopmentTests
{
    [Fact]
    public async Task Development_ServesSwagger()
    {
        await using var factory = new DevelopmentFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/health/live", json);
        Assert.Contains("/health", json);
    }

    private sealed class DevelopmentFactory : FinQueryApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Development");
            builder.UseSetting("SKIP_DOTENV", "true");
        }
    }
}
