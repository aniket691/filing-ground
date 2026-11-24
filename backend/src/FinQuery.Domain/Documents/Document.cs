namespace FinQuery.Domain.Documents;

/// <summary>
/// Row in the existing <c>documents</c> table. Counted by <c>user_id</c>.
/// Child tables (pages, sections, chunks) are not mapped in this module.
/// </summary>
public sealed class Document
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? CompanyId { get; set; }

    public string DocumentType { get; set; } = "";

    public string Title { get; set; } = "";

    public string? SourceUrl { get; set; }

    public string? SourceName { get; set; }

    public DateOnly? FilingDate { get; set; }

    public DateOnly? ReportingPeriodStart { get; set; }

    public DateOnly? ReportingPeriodEnd { get; set; }

    public int? FiscalYear { get; set; }

    public string? FiscalQuarter { get; set; }

    public string? Currency { get; set; }

    public string Language { get; set; } = "en";

    public string? FilePath { get; set; }

    public string? FileHash { get; set; }

    public int? PageCount { get; set; }

    public string ProcessingStatus { get; set; } = "pending";

    public DateTime? ProcessingStartedAt { get; set; }

    public DateTime? ProcessingCompletedAt { get; set; }

    public string? ProcessingError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
