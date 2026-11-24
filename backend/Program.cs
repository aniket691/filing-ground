using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Mvc.Versioning;

var builder = WebApplication.CreateBuilder(args);

// Configure Entity Framework Core with SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite("Data Source=finance_rag.db");
});

// Add CORS policy for Angular development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDevelopment",
        policy =>
        {
            policy.WithOrigins(
                "http://localhost:4200",
                "http://127.0.0.1:4200",
                "http://localhost:8080",
                "http://127.0.0.1:8080")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
});

// Configure JWT Authentication
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings?.Issuer,
        ValidAudience = jwtSettings?.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings?.Key ?? "change-me-to-a-long-random-string"))
    };
});

// Add Authorization services
builder.Services.AddAuthorization();

// Add API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

// Add versioned API explorer (required for Swagger UI)
builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "vVv";
    options.SubstituteApiVersionInUrl = true;
});

// Add OpenAPI/Swagger generation with version support
builder.Services.AddOpenApi(options =>
{
    options.TagActionsBy = (apiDescriptions) => apiDescriptions.Select(action => action.ControllerName);
    options.DocumentName = "v1";
});

// Map versioned API group
var api = builder.WebApplication.MapGroup("api/v{version:apiVersion}");

// Health check endpoint (public, v1)
api.MapGet("/health", () =>
{
    return new
    {
        status = "ok",
        pipeline_ready = true,
        chat_mode = "dummy",
        groq_configured = false,
        jina_configured = false
    };
})
.WithOpenApi(options => options.Tag("Health"));

// Company endpoints (v1)
api.MapGet("/companies", async (AppDbContext db) =>
    await db.Companies.ToListAsync())
    .WithName("GetCompanies")
    .WithTags("companies")
    .HasApiVersion(new ApiVersion(1, 0));

api.MapPost("/companies", (Company company) =>
{
    company.Id = 1;
    return Results.Created($"/v1/companies/{company.Id}", company);
})
.WithName("CreateCompany")
.WithTags("companies")
.HasApiVersion(new ApiVersion(1, 0));

api.MapPatch("/companies/{companyId}", (int companyId, CompanyUpdateDto updateDto) =>
{
    return Results.Ok(new { id = companyId });
})
.WithName("UpdateCompany")
.WithTags("companies")
.HasApiVersion(new ApiVersion(1, 0));

api.MapGet("/companies/{companyId}", (int companyId) =>
{
    return Results.Ok(new { id = companyId, legal_name = "Sample Company" });
})
.WithName("GetCompany")
.WithTags("companies")
.HasApiVersion(new ApiVersion(1, 0));

// Dashboard stats endpoint (v1, authenticated)
api.MapGet("/stats/dashboard", async (AppDbContext db, string? userId) =>
{
    var documentCount = await db.Documents.CountAsync();
    var companyCount = await db.Companies.CountAsync();
    var metricCount = await db.FinancialMetrics.CountAsync();

    return new
    {
        documents = documentCount,
        companies = companyCount,
        financial_metrics = metricCount
    };
})
.WithName("DashboardStats")
.WithTags("stats")
.HasApiVersion(new ApiVersion(1, 0));

// Financial metrics endpoints (v1, authenticated)
api.MapPost("/companies/{companyId}/metrics", (int companyId, FinancialMetric metric) =>
{
    metric.CompanyId = companyId;
    metric.NormalizedMetricName = metric.MetricName.ToLower().Replace(" ", "_");
    return Results.Created($"/v1/companies/{companyId}/metrics/{metric.Id}", metric);
})
.WithName("CreateMetric")
.WithTags("financial-metrics")
.HasApiVersion(new ApiVersion(1, 0));

api.MapGet("/companies/{companyId}/metrics", async (int companyId, AppDbContext db) =>
    await db.FinancialMetrics.Where(m => m.CompanyId == companyId).ToListAsync())
.WithName("ListMetrics")
.WithTags("financial-metrics")
.HasApiVersion(new ApiVersion(1, 0));

// Authentication endpoints (v1, public)
api.MapPost("/auth/register", (User user, JwtSettings jwtSettings) =>
{
    var userId = 1;
    var token = GenerateJwtToken(userId, jwtSettings);
    return Results.Ok(new { id = userId, email = user.Email, token });
})
.WithName("Register")
.WithTags("auth")
.HasApiVersion(new ApiVersion(1, 0));

api.MapPost("/auth/login", (LoginRequest request, JwtSettings jwtSettings) =>
{
    var userId = 1;
    var token = GenerateJwtToken(userId, jwtSettings);
    return Results.Ok(new { id = userId, token });
})
.WithName("Login")
.WithTags("auth")
.HasApiVersion(new ApiVersion(1, 0));

// Document endpoints (v1, authenticated)
api.MapPost("/documents/upload", (IFormFile file, int? companyId, AppDbContext db) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("No file received");
    }

    var fileName = $"{Guid.NewGuid()}_{file.FileName}";
    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);

    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        file.CopyTo(stream);
    }

    var document = new Document
    {
        Id = 1,
        FileName = file.FileName,
        StoragePath = filePath,
        FileSize = file.Length,
        ProcessingStatus = "uploaded",
        CompanyId = companyId ?? 0,
        NormalizedMetricName = Path.GetFileNameWithoutExtension(file.FileName).ToLower().Replace(" ", "_")
    };

    db.Documents.Add(document);
    db.SaveChanges();

    return Results.Ok(new
    {
        document_id = document.Id,
        file_name = document.FileName,
        file_size = document.FileSize,
        processing_status = document.ProcessingStatus
    });
})
.WithName("UploadDocument")
.WithTags("documents")
.HasApiVersion(new ApiVersion(1, 0));

api.MapGet("/documents", async (AppDbContext db) =>
    await db.Documents.ToListAsync())
.WithName("GetDocuments")
.WithTags("documents")
.HasApiVersion(new ApiVersion(1, 0));

api.MapPost("/documents/{documentId}/process", (int documentId, BackgroundTask backgroundTask) =>
{
    return Results.Accepted($"/v1/documents/{documentId}/status", new { document_id = documentId, status = "queued" });
})
.WithName("ProcessDocument")
.WithTags("documents")
.HasApiVersion(new ApiVersion(1, 0));

api.MapGet("/documents/{documentId}/status", (int documentId) =>
{
    return Results.Ok(new
    {
        document_id = documentId,
        status = "processed",
        page_count = 0,
        processing_started_at = null,
        processing_completed_at = null,
        error = null
    });
})
.WithName("GetDocumentStatus")
.WithTags("documents")
.HasApiVersion(new ApiVersion(1, 0));

// RAG Query endpoint (v1, authenticated)
api.MapPost("/rag/query", (RagQueryRequest request, AppDbContext db) =>
{
    return Results.Ok(new RagQueryResponse
    {
        answer = "This is a stub RAG response. In production, this would use embeddings and vector search to answer the question.",
        sources = new List<RagSource>
        {
            new RagSource { document_id = 1, snippet = "Sample document snippet about finance", relevance_score = 0.95 }
        },
        confidence = 0.85,
        processing_time_ms = 120
    });
})
.WithName("RagQuery")
.WithTags("rag")
.HasApiVersion(new ApiVersion(1, 0));

builder.WebApplication.Run();