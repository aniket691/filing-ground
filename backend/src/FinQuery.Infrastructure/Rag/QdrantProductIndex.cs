using FinQuery.Application.Rag;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace FinQuery.Infrastructure.Rag;

/// <summary>
/// Talks to a Qdrant server. The official .NET client has no embedded mode,
/// so an empty <c>QDRANT_URL</c> uses <c>localhost:6334</c>. Search failures
/// return no hits, matching the Python retriever.
/// </summary>
public sealed class QdrantProductIndex(ProductRagSettings settings) : IProductIndex
{
    private readonly QdrantClient _client = Create(settings);

    public void DeleteDocument(int documentId)
    {
        try
        {
            _client.DeleteAsync(settings.Collection, MatchDocument(documentId)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Collection may not exist yet.
        }
    }

    public int Upsert(IReadOnlyList<IndexedChunk> chunks)
    {
        var points = chunks.Where(chunk => chunk.Vector.Length > 0).Select(chunk => new PointStruct
        {
            Id = (ulong)chunk.Id,
            Vectors = chunk.Vector,
            Payload =
            {
                ["document_id"] = chunk.DocumentId,
                ["section_id"] = chunk.SectionId ?? 0,
                ["page_start"] = chunk.PageStart ?? 0,
                ["page_end"] = chunk.PageEnd ?? 0,
                ["chunk_type"] = chunk.ChunkType,
                ["text"] = chunk.Text,
                ["chunk_index"] = chunk.ChunkIndex,
                ["embedding_model"] = chunk.EmbeddingModel ?? ""
            }
        }).ToList();
        if (points.Count == 0)
        {
            return 0;
        }

        Ensure(points[0].Vectors.Vector.Data.Count);
        _client.UpsertAsync(settings.Collection, points).GetAwaiter().GetResult();
        return points.Count;
    }

    public IReadOnlyList<RetrievedPassage> Search(float[] embedding, IReadOnlyList<int> documentIds, int topK)
    {
        if (embedding.Length == 0 || documentIds.Count == 0 || topK < 1)
        {
            return [];
        }

        try
        {
            var filter = new Filter();
            var match = new Match();
            foreach (var id in documentIds)
            {
                match.Integers.Integers.Add(id);
            }

            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = "document_id", Match = match }
            });
            var hits = _client.SearchAsync(settings.Collection, embedding, filter: filter, limit: (ulong)topK).GetAwaiter().GetResult();
            return hits.Select(hit => new RetrievedPassage(
                (long)hit.Id.Num,
                hit.Score,
                hit.Payload.TryGetValue("text", out var text) ? text.StringValue : "",
                hit.Payload.TryGetValue("document_id", out var documentId) ? (int)documentId.IntegerValue : null,
                hit.Payload.TryGetValue("section_id", out var sectionId) && sectionId.IntegerValue != 0 ? sectionId.IntegerValue : null,
                Page(hit, "page_start"),
                Page(hit, "page_end"))).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void Ensure(int size)
    {
        if (_client.CollectionExistsAsync(settings.Collection).GetAwaiter().GetResult())
        {
            return;
        }

        _client.CreateCollectionAsync(settings.Collection, new VectorParams { Size = (ulong)size, Distance = Distance.Cosine }).GetAwaiter().GetResult();
    }

    private static Filter MatchDocument(int documentId)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "document_id",
                Match = new Match { Integer = documentId }
            }
        });
        return filter;
    }

    private static int? Page(ScoredPoint hit, string key)
    {
        if (!hit.Payload.TryGetValue(key, out var value) || value.IntegerValue == 0)
        {
            return null;
        }

        return (int)value.IntegerValue;
    }

    private static QdrantClient Create(ProductRagSettings settings)
    {
        var url = settings.QdrantUrl.Trim();
        if (url.Length == 0 || url.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return new QdrantClient("localhost", apiKey: NullIfEmpty(settings.QdrantApiKey));
        }

        var parsed = new Uri(url);
        if (parsed.Scheme is not ("http" or "https") || string.IsNullOrEmpty(parsed.Host))
        {
            throw new InvalidOperationException($"Invalid QDRANT_URL '{url}': expected http(s)://host[:port]");
        }

        var port = parsed.Port > 0 ? parsed.Port : parsed.Scheme == "https" ? 6334 : 6334;
        if (parsed.Port == 6333)
        {
            port = 6334;
        }

        return new QdrantClient(parsed.Host, port: port, https: parsed.Scheme == "https", apiKey: NullIfEmpty(settings.QdrantApiKey));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
