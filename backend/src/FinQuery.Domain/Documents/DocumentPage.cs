namespace FinQuery.Domain.Documents;

public sealed class DocumentPage
{
    public long Id { get; set; }

    public int DocumentId { get; set; }

    public int PageNumber { get; set; }

    public string? RawText { get; set; }

    public string? CleanedText { get; set; }

    public string ExtractionMethod { get; set; } = "text";

    public string? ExtractionMetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
