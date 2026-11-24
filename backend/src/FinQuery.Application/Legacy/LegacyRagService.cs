using System.Diagnostics;
using FinQuery.Application.Health;

namespace FinQuery.Application.Legacy;

public sealed class LegacyQueryFailure(string message) : Exception(message);

public sealed class LegacyRagService(
    FinQuerySettings settings,
    LegacyPaths paths,
    ILegacyVectorStore store,
    ILegacyEmbedder embedder,
    ILegacyGenerator generator)
{
    public const string NotInitialized = "Pipeline not initialized";
    public const string NothingIngested = "No documents ingested. Call POST /ingest first.";

    public string Mode => HealthStatusReader.NormalizeMode(settings.ChatMode);

    public async Task<LegacyQueryResult> QueryAsync(string question, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        string answer;
        IReadOnlyList<LegacySource> sources;
        var chunks = 0;
        string model;
        if (Mode == "rag")
        {
            if (!store.IsInitialized)
            {
                throw new LegacyQueryFailure(NotInitialized);
            }

            if (!store.IsReady())
            {
                throw new LegacyQueryFailure(NothingIngested);
            }

            var expanded = Expand(question);
            var embedding = embedder.Embed([expanded])[0];
            var hits = store.Query(embedding, paths.TopK);
            var unique = new List<(string Text, IReadOnlyDictionary<string, object> Metadata, float Distance)>();
            var seen = new HashSet<string>();
            foreach (var hit in hits)
            {
                if (seen.Add(hit.Text.Trim()))
                {
                    unique.Add(hit);
                }
            }

            var context = string.Join("\n\n", unique.Select(hit =>
            {
                var source = hit.Metadata.TryGetValue("source", out var value) ? value?.ToString() : null;
                return $"Source: {source ?? "unknown"}\n{hit.Text}";
            }));
            answer = generator.Generate(question, context, out model);
            sources = unique.Select(hit =>
            {
                var source = hit.Metadata.TryGetValue("source", out var value) ? value?.ToString() : null;
                return new LegacySource(
                    string.IsNullOrEmpty(source) ? "unknown" : source,
                    (double)decimal.Round((decimal)(1d - hit.Distance), 3, MidpointRounding.ToEven),
                    hit.Text.Length <= 200 ? hit.Text : hit.Text[..200]);
            }).ToList();
            chunks = unique.Count;
        }
        else
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            var stripped = question.Trim();
            answer = "This is a **dummy reply** for: \"" + stripped + "\"\n\n"
                + "The chat UI is wired. Next steps for real RAG:\n"
                + "1. Ingest documents (`POST /ingest`)\n"
                + "2. Set `CHAT_MODE=rag` in `.env`\n"
                + "3. Configure valid `GROQ_API_KEY` and `JINA_API_KEY`\n\n"
                + "Until then, every send returns a local dummy answer.";
            sources = [new LegacySource("dummy://finquery", 1.0, "Placeholder source while RAG is not enabled.")];
            model = "dummy";
        }

        var elapsed = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2, MidpointRounding.ToEven);
        return new LegacyQueryResult(question.Trim(), answer, sources, chunks, model, elapsed);
    }

    public LegacyIngestResult Ingest(IReadOnlyList<string>? filePaths)
    {
        if (!store.IsInitialized)
        {
            throw new LegacyQueryFailure(NotInitialized);
        }

        var documents = LegacyLoader.LoadForIngest(filePaths, paths);
        var chunks = LegacyChunker.Chunk(documents, paths.ChunkSize, paths.ChunkOverlap);
        if (chunks.Count > 0)
        {
            var embeddings = embedder.Embed(chunks.Select(chunk => chunk.Text).ToList());
            store.Upsert(chunks, embeddings);
        }

        var sources = documents
            .Select(document => document.Metadata.TryGetValue("source", out var source) ? source?.ToString() ?? "" : "")
            .Where(source => source.Length > 0)
            .Distinct()
            .ToList();
        return new LegacyIngestResult("success", chunks.Count, sources, "Documents ingested successfully");
    }

    public LegacyDocumentsResult Documents()
    {
        if (!store.IsInitialized)
        {
            throw new LegacyQueryFailure(NotInitialized);
        }

        return new LegacyDocumentsResult(store.Count(), paths.CollectionName, paths.EmbeddingModel, "Jina AI (cloud)");
    }

    public LegacyResetResult Reset()
    {
        if (!store.IsInitialized)
        {
            throw new LegacyQueryFailure(NotInitialized);
        }

        store.DeleteCollection();
        return new LegacyResetResult("cleared", "Vectorstore cleared. Re-run ingest to use again.");
    }

    private static readonly (string Keyword, string Expansion)[] Expansions =
    [
        ("revenue", "total net sales net sales"),
        ("sales", "net sales total net sales"),
        ("income", "net sales revenue earnings"),
        ("cash", "cash equivalents marketable securities liquidity"),
        ("debt", "long-term debt borrowings"),
        ("r&d", "research and development expense"),
        ("rd", "research and development expense"),
        ("risk", "risk factors ITEM 1A"),
        ("margin", "gross margin profitability")
    ];

    public static string Expand(string query)
    {
        var lower = query.ToLowerInvariant();
        var hints = Expansions.Where(item => lower.Contains(item.Keyword, StringComparison.Ordinal)).Select(item => item.Expansion).ToList();
        return hints.Count == 0 ? query : $"{query} {string.Join(' ', hints)}";
    }
}
