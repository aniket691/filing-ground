using System.ComponentModel.DataAnnotations.Schema;

namespace Backend;

public class FinancialMetric
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int DocumentId { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public string NormalizedMetricName { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public string? Currency { get; set; }
    public int? FiscalYear { get; set; }
    public string? FiscalQuarter { get; set; }
    public int? SourcePage { get; set; }
    public int? SourceChunkId { get; set; }
    public decimal? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }

    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }
}