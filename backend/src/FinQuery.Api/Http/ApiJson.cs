using System.Text.Json;
using System.Text.Json.Serialization;
using FinQuery.Domain.Errors;

namespace FinQuery.Api.Http;

public static class ApiJson
{
    public static JsonSerializerOptions Serializer { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static int StatusCodeFor(DomainError error) => error switch
    {
        NotFoundError => StatusCodes.Status404NotFound,
        ConflictError => StatusCodes.Status409Conflict,
        ValidationError => StatusCodes.Status422UnprocessableEntity,
        AuthenticationError => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest
    };
}

public sealed class ErrorBody
{
    public required string Detail { get; init; }

    /// <summary>Omitted for unexpected 500s. Present for domain errors.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}
