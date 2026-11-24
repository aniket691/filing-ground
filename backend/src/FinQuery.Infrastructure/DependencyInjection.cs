using FinQuery.Application.Auth;
using FinQuery.Application.Companies;
using FinQuery.Application.Documents;
using FinQuery.Application.Ingestion;
using FinQuery.Application.Stats;
using FinQuery.Infrastructure.Auth;
using FinQuery.Infrastructure.Ingestion;
using FinQuery.Infrastructure.Persistence;
using FinQuery.Infrastructure.Stats;
using FinQuery.Application.Rag;
using FinQuery.Infrastructure.Rag;
using FinQuery.Infrastructure.Storage;
using FinQuery.Application.Legacy;
using FinQuery.Infrastructure.Legacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinQuery.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher>(sp =>
            new BcryptPasswordHasher(sp.GetRequiredService<AuthSettings>().BcryptRounds));
        services.AddSingleton<JwtAccessTokenService>();
        services.AddSingleton<IAccessTokenIssuer>(sp => sp.GetRequiredService<JwtAccessTokenService>());
        services.AddSingleton<IAccessTokenReader>(sp => sp.GetRequiredService<JwtAccessTokenService>());
        services.AddScoped<AuthService>();
        return services;
    }

    public static IServiceCollection AddCompanyServices(this IServiceCollection services)
    {
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<CompanyService>();
        return services;
    }

    public static IServiceCollection AddDashboardStats(this IServiceCollection services)
    {
        services.AddScoped<IDashboardStatsReader, DashboardStatsReader>();
        return services;
    }

    public static IServiceCollection AddDocumentReads(this IServiceCollection services)
    {
        services.AddScoped<IDocumentReadRepository, DocumentReadRepository>();
        services.AddScoped<DocumentReadService>();
        services.AddScoped<IDocumentGraphRepository, DocumentGraphRepository>();
        services.AddScoped<DocumentGraphService>();
        return services;
    }

    public static IServiceCollection AddIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IngestionCounters>();
        services.AddSingleton<IIngestionCounters>(provider => provider.GetRequiredService<IngestionCounters>());
        services.AddSingleton<IngestionWorkQueue>();
        services.AddSingleton<IIngestionWorkQueue>(provider => provider.GetRequiredService<IngestionWorkQueue>());
        services.AddScoped<IDocumentProcessRepository, DocumentProcessRepository>();
        services.AddScoped<DocumentProcessService>();
        services.AddScoped<IIngestionPipeline, DocumentIngestionPipeline>();
        services.AddHostedService<IngestionBackgroundService>();
        return services;
    }

    public static IServiceCollection AddDocumentUploads(this IServiceCollection services, StorageSettings storage)
    {
        services.AddSingleton(storage);
        services.AddSingleton<IDocumentFileStore, LocalDocumentFileStore>();
        services.AddScoped<IDocumentWriteRepository, DocumentWriteRepository>();
        services.AddScoped<DocumentUploadService>();
        return services;
    }

    public static IServiceCollection AddLegacyRag(this IServiceCollection services, bool testing, string contentRoot, IConfiguration configuration)
    {
        var repo = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", ".."));
        var vector = configuration["VECTORSTORE_PATH"];
        if (string.IsNullOrWhiteSpace(vector))
        {
            vector = Path.Combine(repo, "backend", "vectorstore", "chroma_db");
        }

        if (testing)
        {
            var empty = Path.Combine(Path.GetTempPath(), "finquery-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            services.AddSingleton(new LegacyPaths(empty, empty, empty));
            services.AddSingleton<MemoryLegacyStore>();
            services.AddSingleton<ILegacyCorpus>(provider => provider.GetRequiredService<MemoryLegacyStore>());
            services.AddSingleton<ILegacyVectorStore>(provider => provider.GetRequiredService<MemoryLegacyStore>());
            services.AddSingleton<ILegacyEmbedder, TestingLegacyEmbedder>();
            services.AddSingleton<ILegacyGenerator, TestingLegacyGenerator>();
        }
        else
        {
            services.AddSingleton(new LegacyPaths(
                Path.Combine(repo, "backend", "data", "raw"),
                Path.Combine(repo, "backend", "sample_docs"),
                vector));
            services.AddSingleton<ChromaLegacyStore>();
            services.AddSingleton<ILegacyCorpus>(provider => provider.GetRequiredService<ChromaLegacyStore>());
            services.AddSingleton<ILegacyVectorStore>(provider => provider.GetRequiredService<ChromaLegacyStore>());
            services.AddHostedService(provider => provider.GetRequiredService<ChromaLegacyStore>());
            services.AddHttpClient<JinaLegacyEmbedder>();
            services.AddHttpClient<GroqLegacyGenerator>();
            services.AddSingleton<ILegacyEmbedder>(provider => provider.GetRequiredService<JinaLegacyEmbedder>());
            services.AddSingleton<ILegacyGenerator>(provider => provider.GetRequiredService<GroqLegacyGenerator>());
        }

        services.AddSingleton<LegacyRagService>();
        return services;
    }

    public static IServiceCollection AddProductRag(this IServiceCollection services, bool testing, IConfiguration configuration, FinQuery.Application.Health.FinQuerySettings settings)
    {
        var provider = testing ? "null" : configuration["EMBEDDING_PROVIDER"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = "jina";
        }

        var apiKey = configuration["EMBEDDING_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = configuration["JINA_API_KEY"];
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.JinaApiKey;
        }

        services.AddSingleton(new ProductRagSettings
        {
            Provider = provider,
            ApiKey = apiKey ?? "",
            Model = string.IsNullOrWhiteSpace(configuration["EMBEDDING_MODEL"]) ? "jina-embeddings-v2-base-en" : configuration["EMBEDDING_MODEL"]!,
            BatchSize = int.TryParse(configuration["EMBEDDING_BATCH_SIZE"], out var batch) && batch > 0 ? batch : 32,
            QdrantUrl = configuration["QDRANT_URL"] ?? "",
            QdrantApiKey = configuration["QDRANT_API_KEY"] ?? "",
            Collection = string.IsNullOrWhiteSpace(configuration["QDRANT_COLLECTION"]) ? "document_chunks" : configuration["QDRANT_COLLECTION"]!,
            GroqApiKey = settings.GroqApiKey,
            LlmModel = settings.LlmModel
        });
        services.AddScoped<IChunkEmbeddingStore, ChunkEmbeddingStore>();
        services.AddScoped<IUserDocumentIds, UserDocumentIds>();
        services.AddSingleton<ProductRagService>();
        if (testing)
        {
            services.AddSingleton<MemoryProductIndex>();
            services.AddSingleton<IProductIndex>(providerServices => providerServices.GetRequiredService<MemoryProductIndex>());
            services.AddSingleton<IProductEmbedder, NullProductEmbedder>();
            services.AddSingleton<IProductLlm, ScriptedProductLlm>();
        }
        else
        {
            services.AddHttpClient<JinaProductEmbedder>();
            services.AddHttpClient<GroqProductLlm>();
            services.AddSingleton<IProductEmbedder>(providerServices => providerServices.GetRequiredService<JinaProductEmbedder>());
            services.AddSingleton<IProductLlm>(providerServices => providerServices.GetRequiredService<GroqProductLlm>());
            services.AddSingleton<QdrantProductIndex>();
            services.AddSingleton<IProductIndex>(providerServices => providerServices.GetRequiredService<QdrantProductIndex>());
        }

        return services;
    }

    /// <summary>
    /// Opens the existing database. Does not create or migrate a schema.
    /// </summary>
    public static IServiceCollection AddFinQueryMySql(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinQueryDbContext>(options =>
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));
        return services;
    }

    public static IServiceCollection AddFinQuerySqlite(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinQueryDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }
}
