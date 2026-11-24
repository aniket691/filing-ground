namespace FinQuery.Application.Legacy;

public sealed class LegacyPaths
{
    public LegacyPaths(string rawDocsPath, string sampleDocsPath, string vectorStorePath)
    {
        RawDocsPath = rawDocsPath;
        SampleDocsPath = sampleDocsPath;
        VectorStorePath = vectorStorePath;
    }

    public string RawDocsPath { get; }

    public string SampleDocsPath { get; }

    public string VectorStorePath { get; }

    public string CollectionName { get; init; } = "financial_docs";

    public int ChunkSize { get; init; } = 400;

    public int ChunkOverlap { get; init; } = 50;

    public int TopK { get; init; } = 5;

    public string EmbeddingModel { get; init; } = "jina-embeddings-v2-base-en";

    public string JinaUrl { get; init; } = "https://api.jina.ai/v1/embeddings";
}

public sealed record LegacySource(string Filename, double RelevanceScore, string Snippet);

public sealed record LegacyQueryResult(
    string Question,
    string Answer,
    IReadOnlyList<LegacySource> Sources,
    int ContextChunks,
    string Model,
    double ProcessingTimeMs);

public sealed record LegacyIngestResult(string Status, int ChunksAdded, IReadOnlyList<string> Sources, string Message);

public sealed record LegacyDocumentsResult(int TotalDocuments, string CollectionName, string EmbeddingModel, string EmbeddingApi);

public sealed record LegacyResetResult(string Status, string Message);

public sealed record LegacyChunk(string Text, IReadOnlyDictionary<string, object> Metadata);

public interface ILegacyCorpus
{
    bool IsInitialized { get; }

    bool IsReady();

    int Count();
}

public interface ILegacyVectorStore : ILegacyCorpus
{
    void Upsert(IReadOnlyList<LegacyChunk> chunks, IReadOnlyList<float[]> embeddings);

    IReadOnlyList<(string Text, IReadOnlyDictionary<string, object> Metadata, float Distance)> Query(float[] embedding, int topK);

    void DeleteCollection();
}

public interface ILegacyEmbedder
{
    float[][] Embed(IReadOnlyList<string> texts);
}

public interface ILegacyGenerator
{
    string Generate(string question, string context, out string model);
}
