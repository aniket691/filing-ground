using FinQuery.Api.Auth;
using FinQuery.Api.Configuration;
using FinQuery.Api.Http;
using FinQuery.Application.Auth;
using FinQuery.Application.Health;
using FinQuery.Domain.Errors;
using FinQuery.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

var skipDotEnv = builder.Environment.IsEnvironment("Testing")
    || string.Equals(builder.Configuration["SKIP_DOTENV"], "true", StringComparison.OrdinalIgnoreCase);
if (!skipDotEnv)
{
    DotEnvLoader.Apply(builder.Configuration);
}

var settings = FinQuerySettingsBinder.Bind(builder.Configuration);
var authSettings = AuthAndDatabaseSettings.BindAuth(builder.Configuration);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(authSettings);
builder.Services.AddSingleton<IHealthStatusReader, HealthStatusReader>();
builder.Services.AddAuthServices();
builder.Services.AddCompanyServices();
builder.Services.AddDashboardStats();
builder.Services.AddDocumentReads();
builder.Services.AddIngestion();
builder.Services.AddDocumentUploads(StorageSettingsBinder.Bind(builder.Configuration, builder.Environment));
builder.Services.AddLegacyRag(builder.Environment.IsEnvironment("Testing"), builder.Environment.ContentRootPath, builder.Configuration);
builder.Services.AddProductRag(builder.Environment.IsEnvironment("Testing"), builder.Configuration, settings);

if (!builder.Environment.IsEnvironment("Testing"))
{
    var database = AuthAndDatabaseSettings.BindDatabase(builder.Configuration);
    if (database.MySqlConnectionString is not null)
    {
        builder.Services.AddFinQueryMySql(database.MySqlConnectionString);
    }
    else if (database.SqliteConnectionString is not null)
    {
        builder.Services.AddFinQuerySqlite(database.SqliteConnectionString);
    }
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = ApiJson.Serializer.PropertyNamingPolicy;
    options.JsonSerializerOptions.DictionaryKeyPolicy = ApiJson.Serializer.DictionaryKeyPolicy;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FinQuery RAG API",
        Version = "1.0.0"
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        if (settings.CorsOrigins.Count == 0)
        {
            return;
        }

        policy.WithOrigins(settings.CorsOrigins.ToArray())
            .WithMethods("GET", "POST", "PATCH", "DELETE")
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors("frontend");
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FinQuery RAG API");
    });
}

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/__test/me", async (HttpContext http, IAccessTokenReader tokens, AuthService authService, CancellationToken cancellationToken) =>
    {
        var user = await CurrentUser.RequireAsync(http.Request, tokens, authService, cancellationToken);
        return Results.Json(user, ApiJson.Serializer);
    });

    app.MapGet("/__test/error", (string kind) =>
    {
        throw kind switch
        {
            "not-found" => new NotFoundError("Document", 9),
            "conflict" => new ConflictError("An account with this email already exists."),
            "validation" => new ValidationError("Question must not be empty"),
            "auth" => new AuthenticationError("Invalid email or password."),
            _ => new InvalidOperationException("boom")
        };
    });
}

app.MapControllers();
app.Run();

public partial class Program;
