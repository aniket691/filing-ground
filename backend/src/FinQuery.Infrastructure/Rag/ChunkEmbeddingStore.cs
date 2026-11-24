using System.Text.Json;
using FinQuery.Application.Rag;
using FinQuery.Domain.Documents;
using FinQuery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Rag;

public sealed class UserDocumentIds(FinQueryDbContext db) : IUserDocumentIds
{
    public async Task<IReadOnlyList<int>> ListAsync(int userId, CancellationToken cancellationToken) =>
        await db.Documents.Where(document => document.UserId == userId).Select(document => document.Id).ToListAsync(cancellationToken);
}

public sealed class ChunkEmbeddingStore(FinQueryDbContext db, IProductEmbedder embedder, IProductIndex index, ProductRagSettings settings) : IChunkEmbeddingStore
{
    public async Task<EmbedStats> EmbedDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        var pending = await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId && chunk.EmbeddingModel == null)
            .OrderBy(chunk => chunk.Id)
            .ToListAsync(cancellationToken);
        var embeddable = pending.Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text)).ToList();
        var skipped = pending.Count - embeddable.Count;
        var embedded = 0;
        var failed = 0;
        var batchSize = Math.Max(1, settings.BatchSize);
        for (var offset = 0; offset < embeddable.Count; offset += batchSize)
        {
            var batch = embeddable.Skip(offset).Take(batchSize).ToList();
            float[][] vectors;
            try
            {
                vectors = embedder.Embed(batch.Select(chunk => chunk.Text).ToList());
                if (vectors.Length != batch.Count)
                {
                    throw new InvalidOperationException($"provider returned {vectors.Length} vectors for {batch.Count} inputs");
                }
            }
            catch (Exception)
            {
                failed += batch.Count;
                continue;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                batch[i].EmbeddingVectorJson = JsonSerializer.Serialize(vectors[i]);
                batch[i].EmbeddingModel = embedder.ModelName;
                batch[i].EmbeddingId = EmbeddingIds.For(embedder.ModelName, batch[i].Id);
                batch[i].EmbeddedAt = DateTime.UtcNow;
                embedded++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return new EmbedStats(embedded, failed, skipped, pending.Count);
    }

    public async Task<int> IndexDocumentAsync(int documentId, CancellationToken cancellationToken)
    {
        index.DeleteDocument(documentId);
        var chunks = await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId && chunk.EmbeddingVectorJson != null)
            .ToListAsync(cancellationToken);
        var points = new List<IndexedChunk>();
        foreach (var chunk in chunks)
        {
            var vector = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingVectorJson!);
            if (vector is null || vector.Length == 0)
            {
                continue;
            }

            points.Add(new IndexedChunk(
                chunk.Id,
                chunk.DocumentId,
                chunk.SectionId,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.ChunkType,
                chunk.Text,
                chunk.ChunkIndex,
                chunk.EmbeddingModel,
                vector));
        }

        return points.Count == 0 ? 0 : index.Upsert(points);
    }
}

public sealed class JinaProductEmbedder(ProductRagSettings settings, HttpClient http) : IProductEmbedder
{
    public string ModelName => settings.Model;

    public float[][] Embed(IReadOnlyList<string> texts)
    {
        if (!string.Equals(settings.Provider, "jina", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown EMBEDDING_PROVIDER: '{settings.Provider}'");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new RuntimeError(
                "No embedding API key configured — set EMBEDDING_API_KEY (or JINA_API_KEY) in the environment.");
        }

        if (texts.Count == 0)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = System.Net.Http.Json.JsonContent.Create(new { model = settings.Model, input = texts });
        using var response = http.Send(request);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(body);
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray())
            .ToArray();
    }
}

public sealed class GroqProductLlm(ProductRagSettings settings, HttpClient http) : IProductLlm
{
    public string Complete(string question, string context)
    {
        if (string.IsNullOrWhiteSpace(settings.GroqApiKey))
        {
            throw new InvalidOperationException("GROQ_API_KEY not configured");
        }

        var system = """
            You are FinQuery, a financial document assistant.

            STRICT RULES:
            1. Answer ONLY using the numbered context passages below.
            2. Cite figures exactly as they appear in the context.
            3. If the context does not contain the answer, your ENTIRE reply must be exactly:
            I don't have enough information in the provided documents.
            4. Never fabricate financial data.
            5. Keep the answer concise; use bullet points for lists.
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.GroqApiKey);
        request.Content = System.Net.Http.Json.JsonContent.Create(new
        {
            model = settings.LlmModel,
            temperature = 0.1,
            max_tokens = 1024,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = $"Question: {question}\n\nContext:\n{context}" }
            }
        });
        using var response = http.Send(request);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(body);
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
