namespace FinQuery.Application.Health;

/// <summary>
/// Body of <c>GET /health</c>. Names match the FastAPI <c>HealthResponse</c>.
/// </summary>
public sealed record HealthReport(
    string Status,
    bool PipelineReady,
    int TotalChunks,
    string LlmModel,
    bool GroqConfigured,
    bool JinaConfigured,
    string ChatMode);

public sealed record LiveReport(string Status);
