using System.ComponentModel.DataAnnotations.Schema;

namespace Backend;

public class Document
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "Other";
    public string? SourceUrl { get; set; }
    public string? SourceName { get; set; }
    public DateTime? FilingDate { get; set; }
    public int? FiscalYear { get; set; }
    public string? FiscalQuarter { get; set; }
    public string? Currency { get; set; }
    public string Language { get; set; } = "en";
    public string? FilePath { get; set; }
    public string? FileHash { get; set; }
    public string ProcessingStatus { get; set; } = "uploaded";
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public string? ProcessingError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("UserId")]
    public virtual User? User { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }

    public virtual ICollection<FinancialMetric> FinancialMetrics { get; set; } = new List<FinancialMetric>();
}