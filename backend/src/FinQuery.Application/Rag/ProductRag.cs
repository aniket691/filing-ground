using System.Security.Cryptography;
using System.Text;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Rag;

public sealed class ProductRagSettings
{
    public string Provider { get; init; } = "jina";

    public string ApiKey { get; init; } = "";

    public string Model { get; init; } = "jina-embeddings-v2-base-en";

    public string ApiUrl { get; init; } = "https://api.jina.ai/v1/embeddings";

    public int BatchSize { get; init; } = 32;

    public int TopK { get; init; } = 5;

    public string QdrantUrl { get; init; } = "";

    public string QdrantApiKey { get; init; } = "";

    public string Collection { get; init; } = "document_chunks";

    public int Dimensions { get; init; } = 768;

    public string GroqApiKey { get; init; } = "";

    public string LlmModel { get; init; } = "openai/gpt-oss-120b";
}

public sealed record EmbedStats(int Embedded, int Failed, int SkippedEmpty, int Pending);

public sealed record IndexedChunk(
    long Id,
    int DocumentId,
    long? SectionId,
    int? PageStart,
    int? PageEnd,
    string ChunkType,
    string Text,
    int ChunkIndex,
    string? EmbeddingModel,
    float[] Vector);

public sealed record RetrievedPassage(
    long ChunkId,
    double Score,
    string Text,
    int? DocumentId,
    long? SectionId,
    int? PageStart,
    int? PageEnd);

public sealed record RagSource(int? DocumentId, int? PageStart, int? PageEnd, double Score);

public sealed record RagAnswer(string Answer, IReadOnlyList<RagSource> Sources);

public interface IProductEmbedder
{
    string ModelName { get; }

    float[][] Embed(IReadOnlyList<string> texts);
}

public interface IProductIndex
{
    void DeleteDocument(int documentId);

    int Upsert(IReadOnlyList<IndexedChunk> chunks);

    IReadOnlyList<RetrievedPassage> Search(float[] embedding, IReadOnlyList<int> documentIds, int topK);
}

public interface IProductLlm
{
    string Complete(string question, string context);
}

public interface IUserDocumentIds
{
    Task<IReadOnlyList<int>> ListAsync(int userId, CancellationToken cancellationToken);
}

public interface IChunkEmbeddingStore
{
    Task<EmbedStats> EmbedDocumentAsync(int documentId, CancellationToken cancellationToken);

    Task<int> IndexDocumentAsync(int documentId, CancellationToken cancellationToken);
}

/// <summary>Python raises <c>RuntimeError</c> when the embedding key is missing.</summary>
public sealed class RuntimeError(string message) : Exception(message);

public static class EmbeddingIds
{
    public static readonly Guid Namespace = Guid.Parse("6f1d2c34-7a8e-4b5f-9c0d-1e2f3a4b5c6d");

    public static string For(string model, long chunkId) => Uuid5(Namespace, $"{model}:{chunkId}").ToString();

    public static Guid Uuid5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        Swap(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);
        var hash = SHA1.HashData(data);
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Swap(bytes);
        return new Guid(bytes);
    }

    private static void Swap(byte[] bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}

public sealed class NullProductEmbedder : IProductEmbedder
{
    public string ModelName => "null-embed-8";

    public float[][] Embed(IReadOnlyList<string> texts) =>
        texts.Select(text =>
        {
            var seed = text.Sum(character => (int)character) % 251;
            return Enumerable.Range(0, 8).Select(index => ((seed + index) % 97) / 97f).ToArray();
        }).ToArray();
}

public sealed class ProductRagService(IProductEmbedder embedder, IProductIndex index, IProductLlm llm, ProductRagSettings settings)
{
    public const string Fallback = "I don't have enough information in the provided documents.";

    public RagAnswer Answer(string question, IReadOnlyList<int> documentIds)
    {
        var asked = (question ?? "").Trim();
        if (asked.Length == 0)
        {
            throw new ValidationError("Question must not be empty");
        }

        float[] vector;
        try
        {
            vector = embedder.Embed([asked])[0];
        }
        catch (Exception)
        {
            throw new ValidationError(
                "Could not embed the question. Check JINA_API_KEY / EMBEDDING_API_KEY on the server.");
        }

        var hits = index.Search(vector, documentIds, settings.TopK);
        var sources = hits.Select(hit => new RagSource(
            hit.DocumentId,
            hit.PageStart,
            hit.PageEnd,
            (double)decimal.Round((decimal)hit.Score, 4, MidpointRounding.ToEven))).ToList();
        if (hits.Count == 0)
        {
            return new RagAnswer(Fallback, []);
        }

        var context = string.Join("\n\n", hits.Select((hit, index) =>
            $"[passage {index + 1}] (document {hit.DocumentId}, pages {hit.PageStart}-{hit.PageEnd})\n{hit.Text}"));
        string answer;
        try
        {
            answer = llm.Complete(asked, context);
        }
        catch (Exception)
        {
            return new RagAnswer(Fallback, sources);
        }

        return string.IsNullOrWhiteSpace(answer)
            ? new RagAnswer(Fallback, sources)
            : new RagAnswer(answer.Trim(), sources);
    }
}

public sealed class MemoryProductIndex : IProductIndex
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, IndexedChunk> _points = [];

    public void DeleteDocument(int documentId)
    {
        lock (_gate)
        {
            foreach (var id in _points.Where(pair => pair.Value.DocumentId == documentId).Select(pair => pair.Key).ToList())
            {
                _points.Remove(id);
            }
        }
    }

    public int Upsert(IReadOnlyList<IndexedChunk> chunks)
    {
        lock (_gate)
        {
            var written = 0;
            foreach (var chunk in chunks)
            {
                if (chunk.Vector.Length == 0)
                {
                    continue;
                }

                _points[chunk.Id] = chunk;
                written++;
            }

            return written;
        }
    }

    public IReadOnlyList<RetrievedPassage> Search(float[] embedding, IReadOnlyList<int> documentIds, int topK)
    {
        if (embedding.Length == 0 || documentIds.Count == 0 || topK < 1)
        {
            return [];
        }

        var allowed = documentIds.ToHashSet();
        lock (_gate)
        {
            return _points.Values
                .Where(point => allowed.Contains(point.DocumentId))
                .Select(point => (Point: point, Score: Cosine(embedding, point.Vector)))
                .OrderByDescending(row => row.Score)
                .Take(topK)
                .Select(row => new RetrievedPassage(
                    row.Point.Id,
                    row.Score,
                    row.Point.Text,
                    row.Point.DocumentId,
                    row.Point.SectionId,
                    row.Point.PageStart,
                    row.Point.PageEnd))
                .ToList();
        }
    }

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}

public sealed class ScriptedProductLlm : IProductLlm
{
    public string Complete(string question, string context) =>
        question.Contains("llm-fail", StringComparison.Ordinal)
            ? throw new InvalidOperationException("llm down")
            : "Revenue was stated in the passage.";
}
