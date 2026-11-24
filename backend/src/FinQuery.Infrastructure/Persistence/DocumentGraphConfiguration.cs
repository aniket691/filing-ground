using FinQuery.Domain.Documents;
using FinQuery.Domain.Metrics;
using FinQuery.Domain.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinQuery.Infrastructure.Persistence;

public sealed class DocumentPageConfiguration : IEntityTypeConfiguration<DocumentPage>
{
    public void Configure(EntityTypeBuilder<DocumentPage> builder)
    {
        builder.ToTable("document_pages");
        builder.HasKey(page => page.Id);
        builder.Property(page => page.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(page => page.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(page => page.PageNumber).HasColumnName("page_number").IsRequired();
        builder.Property(page => page.RawText).HasColumnName("raw_text");
        builder.Property(page => page.CleanedText).HasColumnName("cleaned_text");
        builder.Property(page => page.ExtractionMethod).HasColumnName("extraction_method").HasMaxLength(16).IsRequired();
        builder.Property(page => page.ExtractionMetadataJson).HasColumnName("extraction_metadata");
        builder.Property(page => page.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

public sealed class DocumentSectionConfiguration : IEntityTypeConfiguration<DocumentSection>
{
    public void Configure(EntityTypeBuilder<DocumentSection> builder)
    {
        builder.ToTable("document_sections");
        builder.HasKey(section => section.Id);
        builder.Property(section => section.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(section => section.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(section => section.ParentSectionId).HasColumnName("parent_section_id");
        builder.Property(section => section.SectionTitle).HasColumnName("section_title").HasMaxLength(512).IsRequired();
        builder.Property(section => section.SectionType).HasColumnName("section_type").HasMaxLength(64).IsRequired();
        builder.Property(section => section.PageStart).HasColumnName("page_start");
        builder.Property(section => section.PageEnd).HasColumnName("page_end");
        builder.Property(section => section.Text).HasColumnName("text");
        builder.Property(section => section.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks");
        builder.HasKey(chunk => chunk.Id);
        builder.Property(chunk => chunk.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(chunk => chunk.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(chunk => chunk.SectionId).HasColumnName("section_id");
        builder.Property(chunk => chunk.ChunkIndex).HasColumnName("chunk_index").IsRequired();
        builder.Property(chunk => chunk.ChunkType).HasColumnName("chunk_type").HasMaxLength(32).IsRequired();
        builder.Property(chunk => chunk.Text).HasColumnName("text").IsRequired();
        builder.Property(chunk => chunk.TokenCount).HasColumnName("token_count");
        builder.Property(chunk => chunk.PageStart).HasColumnName("page_start");
        builder.Property(chunk => chunk.PageEnd).HasColumnName("page_end");
        builder.Property(chunk => chunk.MetadataJson).HasColumnName("metadata").IsRequired();
        builder.Property(chunk => chunk.EmbeddingVectorJson).HasColumnName("embedding_vector");
        builder.Property(chunk => chunk.EmbeddingId).HasColumnName("embedding_id").HasMaxLength(64);
        builder.Property(chunk => chunk.EmbeddingModel).HasColumnName("embedding_model").HasMaxLength(128);
        builder.Property(chunk => chunk.EmbeddedAt).HasColumnName("embedded_at");
        builder.Property(chunk => chunk.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}

public sealed class FinancialTableConfiguration : IEntityTypeConfiguration<FinancialTable>
{
    public void Configure(EntityTypeBuilder<FinancialTable> builder)
    {
        builder.ToTable("financial_tables");
        builder.HasKey(table => table.Id);
        builder.Property(table => table.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(table => table.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(table => table.SourceChunkId).HasColumnName("source_chunk_id");
        builder.Property(table => table.PageNumber).HasColumnName("page_number");
        builder.Property(table => table.Title).HasColumnName("title").HasMaxLength(512);
        builder.Property(table => table.HeadersJson).HasColumnName("headers").IsRequired();
        builder.Property(table => table.Units).HasColumnName("units").HasMaxLength(32);
        builder.Property(table => table.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(table => table.ExtractionConfidence).HasColumnName("extraction_confidence").HasPrecision(4, 3);
        builder.Property(table => table.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Ignore(table => table.Rows);
    }
}

public sealed class FinancialTableRowConfiguration : IEntityTypeConfiguration<FinancialTableRow>
{
    public void Configure(EntityTypeBuilder<FinancialTableRow> builder)
    {
        builder.ToTable("financial_table_rows");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(row => row.TableId).HasColumnName("table_id").IsRequired();
        builder.Property(row => row.RowIndex).HasColumnName("row_index").IsRequired();
        builder.Property(row => row.RowLabel).HasColumnName("row_label").HasMaxLength(512);
        builder.Property(row => row.CellsJson).HasColumnName("cells").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
    }
}
