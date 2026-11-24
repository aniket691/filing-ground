using FinQuery.Application.Health;

namespace FinQuery.Api.Configuration;

public static class FinQuerySettingsBinder
{
    public static FinQuerySettings Bind(IConfiguration configuration)
    {
        return new FinQuerySettings
        {
            ChatMode = configuration["CHAT_MODE"] ?? "dummy",
            LlmModel = configuration["LLM_MODEL"]?.Trim() ?? "openai/gpt-oss-120b",
            GroqApiKey = configuration["GROQ_API_KEY"] ?? "",
            JinaApiKey = configuration["JINA_API_KEY"] ?? "",
            CorsOrigins = ParseOrigins(configuration["BACKEND_CORS_ORIGINS"])
        };
    }

    private static IReadOnlyList<string> ParseOrigins(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return
            [
                "http://localhost:5173",
                "http://127.0.0.1:5173"
            ];
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
