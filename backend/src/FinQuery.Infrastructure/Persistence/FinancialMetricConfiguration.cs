using FinQuery.Domain.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinQuery.Infrastructure.Persistence;

/// <summary>
/// Maps the existing <c>financial_metrics</c> columns. Composite indexes and
/// check constraints stay in MySQL and are not declared here.
/// <c>source_chunk_id</c> is a column only until document chunks are mapped.
/// </summary>
public sealed class FinancialMetricConfiguration : IEntityTypeConfiguration<FinancialMetric>
{
    public void Configure(EntityTypeBuilder<FinancialMetric> builder)
    {
        builder.ToTable("financial_metrics");
        builder.HasKey(metric => metric.Id);
        builder.Property(metric => metric.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(metric => metric.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(metric => metric.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(metric => metric.MetricName).HasColumnName("metric_name").HasMaxLength(255).IsRequired();
        builder.Property(metric => metric.NormalizedMetricName).HasColumnName("normalized_metric_name").HasMaxLength(255).IsRequired();
        builder.Property(metric => metric.Value).HasColumnName("value").HasPrecision(30, 10).IsRequired();
        builder.Property(metric => metric.Unit).HasColumnName("unit").HasMaxLength(32);
        builder.Property(metric => metric.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(metric => metric.PeriodStart).HasColumnName("period_start");
        builder.Property(metric => metric.PeriodEnd).HasColumnName("period_end");
        builder.Property(metric => metric.FiscalYear).HasColumnName("fiscal_year");
        builder.Property(metric => metric.FiscalQuarter).HasColumnName("fiscal_quarter").HasMaxLength(2);
        builder.Property(metric => metric.MetricType).HasColumnName("metric_type").HasMaxLength(32);
        builder.Property(metric => metric.SourcePage).HasColumnName("source_page");
        builder.Property(metric => metric.SourceChunkId).HasColumnName("source_chunk_id");
        builder.Property(metric => metric.Confidence).HasColumnName("confidence").HasPrecision(4, 3);
        builder.Property(metric => metric.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
