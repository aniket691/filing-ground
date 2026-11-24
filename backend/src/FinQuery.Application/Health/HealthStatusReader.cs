using FinQuery.Application.Legacy;

namespace FinQuery.Application.Health;

public interface IHealthStatusReader
{
    HealthReport Read();
}

/// <summary>
/// Dummy mode is always ready. Rag mode is ready only when the legacy Chroma
/// collection has at least one row. A store that failed to start counts as absent.
/// </summary>
public sealed class HealthStatusReader(FinQuerySettings settings, ILegacyCorpus? corpus = null) : IHealthStatusReader
{
    public HealthReport Read()
    {
        var mode = NormalizeMode(settings.ChatMode);
        var ready = false;
        var chunks = 0;
        if (corpus is { IsInitialized: true })
        {
            try
            {
                ready = corpus.IsReady();
                chunks = corpus.Count();
            }
            catch (Exception)
            {
                ready = false;
            }
        }

        if (mode == "dummy")
        {
            ready = true;
        }

        return new HealthReport(
            Status: "ok",
            PipelineReady: ready,
            TotalChunks: chunks,
            LlmModel: mode == "rag" ? settings.LlmModel : "dummy",
            GroqConfigured: !string.IsNullOrWhiteSpace(settings.GroqApiKey),
            JinaConfigured: !string.IsNullOrWhiteSpace(settings.JinaApiKey),
            ChatMode: mode);
    }

    /// <summary>Same rule as <c>ChatService.mode</c>: only dummy and rag are valid.</summary>
    public static string NormalizeMode(string? chatMode)
    {
        var mode = (chatMode ?? "").Trim().ToLowerInvariant();
        return mode is "dummy" or "rag" ? mode : "dummy";
    }
}
