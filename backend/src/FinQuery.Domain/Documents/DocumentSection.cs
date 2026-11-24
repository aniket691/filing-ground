namespace FinQuery.Domain.Documents;

public sealed class DocumentSection
{
    public long Id { get; set; }

    public int DocumentId { get; set; }

    public long? ParentSectionId { get; set; }

    public string SectionTitle { get; set; } = "";

    public string SectionType { get; set; } = "";

    public int? PageStart { get; set; }

    public int? PageEnd { get; set; }

    public string? Text { get; set; }

    public DateTime CreatedAt { get; set; }
}
