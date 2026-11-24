namespace FinQuery.Domain.Metrics;

/// <summary>
/// Row in the existing <c>financial_metrics</c> table.
/// Ownership is through <c>company_id</c>, not a user column.
/// </summary>
public sealed class FinancialMetric
{
    public long Id { get; set; }

    public int CompanyId { get; set; }

    public int DocumentId { get; set; }

    public string MetricName { get; set; } = "";

    public string NormalizedMetricName { get; set; } = "";

    public decimal Value { get; set; }

    public string? Unit { get; set; }

    public string? Currency { get; set; }

    public DateOnly? PeriodStart { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    public int? FiscalYear { get; set; }

    public string? FiscalQuarter { get; set; }

    public string? MetricType { get; set; }

    public int? SourcePage { get; set; }

    public long? SourceChunkId { get; set; }

    public decimal? Confidence { get; set; }

    public DateTime CreatedAt { get; set; }
}
