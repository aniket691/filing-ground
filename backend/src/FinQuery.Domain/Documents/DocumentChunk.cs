namespace FinQuery.Domain.Documents;

public sealed class DocumentChunk
{
    public long Id { get; set; }

    public int DocumentId { get; set; }

    public long? SectionId { get; set; }

    public int ChunkIndex { get; set; }

    public string ChunkType { get; set; } = "text";

    public string Text { get; set; } = "";

    public int? TokenCount { get; set; }

    public int? PageStart { get; set; }

    public int? PageEnd { get; set; }

    /// <summary>Column name is <c>metadata</c>. The API calls it <c>chunk_metadata</c>.</summary>
    public string MetadataJson { get; set; } = "{}";

    public string? EmbeddingVectorJson { get; set; }

    public string? EmbeddingId { get; set; }

    public string? EmbeddingModel { get; set; }

    public DateTime? EmbeddedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
