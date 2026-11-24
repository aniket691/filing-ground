using FinQuery.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinQuery.Infrastructure.Persistence;

/// <summary>
/// Maps the existing <c>documents</c> columns used by dashboard counts.
/// The partial unique hash index and the period check stay in MySQL.
/// They are not declared here.
/// </summary>
public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(document => document.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(document => document.CompanyId).HasColumnName("company_id");
        builder.Property(document => document.DocumentType).HasColumnName("document_type").HasMaxLength(64).IsRequired();
        builder.Property(document => document.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
        builder.Property(document => document.SourceUrl).HasColumnName("source_url");
        builder.Property(document => document.SourceName).HasColumnName("source_name").HasMaxLength(255);
        builder.Property(document => document.FilingDate).HasColumnName("filing_date");
        builder.Property(document => document.ReportingPeriodStart).HasColumnName("reporting_period_start");
        builder.Property(document => document.ReportingPeriodEnd).HasColumnName("reporting_period_end");
        builder.Property(document => document.FiscalYear).HasColumnName("fiscal_year");
        builder.Property(document => document.FiscalQuarter).HasColumnName("fiscal_quarter").HasMaxLength(2);
        builder.Property(document => document.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(document => document.Language).HasColumnName("language").HasMaxLength(8).IsRequired();
        builder.Property(document => document.FilePath).HasColumnName("file_path").HasMaxLength(1024);
        builder.Property(document => document.FileHash).HasColumnName("file_hash").HasMaxLength(64);
        builder.Property(document => document.PageCount).HasColumnName("page_count");
        builder.Property(document => document.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(32).IsRequired();
        builder.Property(document => document.ProcessingStartedAt).HasColumnName("processing_started_at");
        builder.Property(document => document.ProcessingCompletedAt).HasColumnName("processing_completed_at");
        builder.Property(document => document.ProcessingError).HasColumnName("processing_error");
        builder.Property(document => document.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(document => document.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
