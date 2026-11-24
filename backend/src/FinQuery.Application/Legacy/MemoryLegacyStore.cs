namespace FinQuery.Application.Legacy;

/// <summary>
/// Used when the host is the test environment. Vectors are deterministic so
/// retrieval does not call Jina, and a question that contains an API-key
/// failure phrase throws that phrase so the route can map it.
/// </summary>
public sealed class TestingLegacyEmbedder : ILegacyEmbedder
{
    public float[][] Embed(IReadOnlyList<string> texts) =>
        texts.Select(text =>
        {
            var vector = new float[8];
            for (var i = 0; i < text.Length; i++)
            {
                vector[i % 8] += text[i];
            }

            return vector;
        }).ToArray();
}

public sealed class TestingLegacyGenerator : ILegacyGenerator
{
    public string Generate(string question, string context, out string model)
    {
        if (question.Contains("expired_api_key", StringComparison.Ordinal) || question.Contains("Invalid API Key", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(question);
        }

        if (question.Contains("groq-down", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("model overloaded");
        }

        model = "test-generator";
        return context.Length == 0 ? "grounded" : context;
    }
}

public sealed class MemoryLegacyStore : ILegacyVectorStore
{
    private readonly Lock _gate = new();
    private List<Row> _rows = [];

    public bool IsInitialized => true;

    public bool IsReady()
    {
        lock (_gate)
        {
            return _rows.Count > 0;
        }
    }

    public int Count()
    {
        lock (_gate)
        {
            return _rows.Count;
        }
    }

    public void Upsert(IReadOnlyList<LegacyChunk> chunks, IReadOnlyList<float[]> embeddings)
    {
        lock (_gate)
        {
            var byId = _rows.ToDictionary(row => row.Id);
            for (var i = 0; i < chunks.Count; i++)
            {
                var id = LegacyChunker.ChunkId(chunks[i].Text);
                byId[id] = new Row(id, chunks[i].Text, chunks[i].Metadata, embeddings[i]);
            }

            _rows = byId.Values.ToList();
        }
    }

    public IReadOnlyList<(string Text, IReadOnlyDictionary<string, object> Metadata, float Distance)> Query(float[] embedding, int topK)
    {
        lock (_gate)
        {
            return _rows
                .Select(row => (row.Text, row.Metadata, Distance(embedding, row.Embedding)))
                .OrderBy(row => row.Item3)
                .Take(topK)
                .ToList();
        }
    }

    public void DeleteCollection()
    {
        lock (_gate)
        {
            _rows = [];
        }
    }

    private static float Distance(float[] left, float[] right)
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
            return 1;
        }

        var cosine = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        return (float)(1 - cosine);
    }

    private sealed record Row(string Id, string Text, IReadOnlyDictionary<string, object> Metadata, float[] Embedding);
}
