namespace FinQuery.Application.Health;

/// <summary>
/// Process settings Module 1 actually reads. Property values come from the
/// same environment names as the Python app (<c>CHAT_MODE</c>, <c>LLM_MODEL</c>,
/// <c>GROQ_API_KEY</c>, <c>JINA_API_KEY</c>).
/// </summary>
public sealed class FinQuerySettings
{
    public string ChatMode { get; init; } = "dummy";

    public string LlmModel { get; init; } = "openai/gpt-oss-120b";

    public string GroqApiKey { get; init; } = "";

    public string JinaApiKey { get; init; } = "";

    public IReadOnlyList<string> CorsOrigins { get; init; } =
    [
        "http://localhost:5173",
        "http://127.0.0.1:5173"
    ];
}
