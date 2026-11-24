using System.Net.Http.Json;
using System.Text.Json;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using ChromaDotNet.Local;
using FinQuery.Application.Health;
using FinQuery.Application.Legacy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinQuery.Infrastructure.Legacy;

public sealed class ChromaLegacyStore(LegacyPaths paths, ILogger<ChromaLegacyStore> logger) : ILegacyVectorStore, IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ChromaLocalServer? _server;
    private ChromaClient? _client;
    private bool _started;

    public bool IsInitialized => _started;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(paths.VectorStorePath);
            _server = await ChromaLocalServer.StartAsync(new ChromaLocalOptions { DataPath = paths.VectorStorePath }, cancellationToken);
            _client = _server.CreateClient();
            _started = true;
            logger.LogInformation("Chroma 1.5.9 is serving {Path}", paths.VectorStorePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Chroma did not start. Legacy RAG routes will report that the pipeline is not initialized.");
            _started = false;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _client?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    public bool IsReady()
    {
        try
        {
            return Count() > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public int Count()
    {
        var collection = CollectionAsync().GetAwaiter().GetResult();
        return (int)collection.CountAsync().GetAwaiter().GetResult();
    }

    public void Upsert(IReadOnlyList<LegacyChunk> chunks, IReadOnlyList<float[]> embeddings)
    {
        var collection = CollectionAsync().GetAwaiter().GetResult();
        var ids = chunks.Select(chunk => LegacyChunker.ChunkId(chunk.Text)).ToList();
        var vectors = embeddings.Select(vector => (ReadOnlyMemory<float>)vector).ToList();
        var documents = chunks.Select(chunk => chunk.Text).ToList();
        var metadata = chunks.Select(chunk => (IReadOnlyDictionary<string, object>)chunk.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value)).ToList();
        collection.UpsertAsync(ids, vectors, metadata, documents).GetAwaiter().GetResult();
    }

    public IReadOnlyList<(string Text, IReadOnlyDictionary<string, object> Metadata, float Distance)> Query(float[] embedding, int topK)
    {
        var collection = CollectionAsync().GetAwaiter().GetResult();
        var hits = collection.QueryAsync(embedding, topK, null!, null!, null, null).GetAwaiter().GetResult();
        return hits.Select(hit => (
            hit.Document ?? "",
            (IReadOnlyDictionary<string, object>)(hit.Metadata ?? new Dictionary<string, object>()),
            hit.Distance ?? 1f)).ToList();
    }

    public void DeleteCollection()
    {
        try
        {
            Client().DeleteCollectionAsync(paths.CollectionName).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Chroma delete failed for {Collection}", paths.CollectionName);
        }
    }

    private ChromaClient Client() => _client ?? throw new InvalidOperationException("Chroma is not running.");

    private async Task<ChromaCollectionClient> CollectionAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await Client().GetOrCreateCollectionClientAsync(
                new ChromaCollectionDefinition(paths.CollectionName)
                {
                    Metadata = new Dictionary<string, object> { ["hnsw:space"] = "cosine" }
                });
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class JinaLegacyEmbedder(FinQuerySettings settings, LegacyPaths paths, HttpClient http) : ILegacyEmbedder
{
    public float[][] Embed(IReadOnlyList<string> texts)
    {
        if (string.IsNullOrWhiteSpace(settings.JinaApiKey))
        {
            throw new InvalidOperationException("JINA_API_KEY not set.");
        }

        var vectors = new List<float[]>();
        for (var offset = 0; offset < texts.Count; offset += 100)
        {
            var batch = texts.Skip(offset).Take(100).ToList();
            using var request = new HttpRequestMessage(HttpMethod.Post, paths.JinaUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.JinaApiKey);
            request.Content = JsonContent.Create(new { model = paths.EmbeddingModel, input = batch });
            using var response = http.Send(request);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(body);
            }

            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
            {
                vectors.Add(item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray());
            }
        }

        return vectors.ToArray();
    }
}

public sealed class GroqLegacyGenerator(FinQuerySettings settings, HttpClient http) : ILegacyGenerator
{
    public const string SystemPrompt = """
        You are FinQuery, an expert AI financial analyst assistant.

        STRICT RULES:
        1. Answer ONLY using the document context provided below.
        2. Cite specific numbers and figures exactly as they appear in the documents.
        3. Treat common financial synonyms as the same concept when the context supports it:
           - "revenue" / "sales" / "total revenue" often means "total net sales" or segment net sales in 10-K filings.
           - "cash" may appear as "cash, cash equivalents, and marketable securities".
           - "R&D" means "research and development" expense.
        4. If the context contains the answer under a different but equivalent label (e.g. net sales for revenue),
           answer directly with those figures — do NOT say you cannot find it.
        5. If nothing relevant exists after applying rule 3, your entire reply must be only:
           "I cannot find this information in the provided documents."
           Never use that sentence if you also provide figures or facts from the context.
        6. Never fabricate financial data, percentages, or figures.
        7. Structure your answer clearly — use bullet points for lists.
        8. State which source file and section your answer comes from.
        """;

    public string Generate(string question, string context, out string model)
    {
        if (string.IsNullOrWhiteSpace(settings.GroqApiKey))
        {
            model = "none";
            return "⚠️ GROQ_API_KEY not configured. Add it to your .env file.";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.GroqApiKey);
        request.Content = JsonContent.Create(new
        {
            model = settings.LlmModel,
            temperature = 0.1,
            max_tokens = 1024,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"Context from financial documents:\n\n{context}\n\nNote: In 10-K filings, total net sales is the usual label for company-wide revenue.\n\nQuestion: {question}" }
            }
        });
        using var response = http.Send(request);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(body);
        }

        using var document = JsonDocument.Parse(body);
        model = settings.LlmModel;
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
