using System.Text.Json;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Metrics;
using FinQuery.Domain.Tables;

namespace FinQuery.Application.Documents;

public sealed record PageResponse(
    long Id,
    int DocumentId,
    int PageNumber,
    string? RawText,
    string? CleanedText,
    string ExtractionMethod,
    JsonElement ExtractionMetadata,
    DateTime CreatedAt);

public sealed record SectionResponse(
    long Id,
    int DocumentId,
    long? ParentSectionId,
    string SectionTitle,
    string SectionType,
    int? PageStart,
    int? PageEnd,
    string? Text,
    DateTime CreatedAt);

public sealed record ChunkResponse(
    long Id,
    int DocumentId,
    long? SectionId,
    int ChunkIndex,
    string ChunkType,
    string Text,
    int? TokenCount,
    int? PageStart,
    int? PageEnd,
    JsonElement ChunkMetadata,
    DateTime CreatedAt);

public sealed record TableRowResponse(
    long Id,
    long TableId,
    int RowIndex,
    string? RowLabel,
    JsonElement Cells,
    DateTime CreatedAt);

public sealed record TableResponse(
    long Id,
    int DocumentId,
    long? SourceChunkId,
    int? PageNumber,
    string? Title,
    JsonElement Headers,
    string? Units,
    string? Currency,
    decimal? ExtractionConfidence,
    DateTime CreatedAt,
    IReadOnlyList<TableRowResponse> Rows);

public sealed record MetricResponse(
    long Id,
    int CompanyId,
    int DocumentId,
    string MetricName,
    string NormalizedMetricName,
    decimal Value,
    string? Unit,
    string? Currency,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int? FiscalYear,
    string? FiscalQuarter,
    string? MetricType,
    int? SourcePage,
    long? SourceChunkId,
    decimal? Confidence,
    DateTime CreatedAt);

public sealed record PageWrite(int PageNumber, string? RawText, string? CleanedText, string ExtractionMethod, string ExtractionMetadataJson);

public sealed record SectionWrite(string SectionTitle, string SectionType, int? PageStart, int? PageEnd, string? Text, long? ParentSectionId);

public sealed record ChunkWrite(int ChunkIndex, string Text, long? SectionId, string ChunkType, int? TokenCount, int? PageStart, int? PageEnd, string MetadataJson);

public sealed record TableRowWrite(int RowIndex, string? RowLabel, string CellsJson);

public sealed record TableWrite(string? Title, int? PageNumber, string HeadersJson, string? Units, string? Currency, long? SourceChunkId, decimal? ExtractionConfidence, IReadOnlyList<TableRowWrite> Rows);

public sealed record MetricWrite(
    int DocumentId,
    string MetricName,
    decimal Value,
    string? Unit,
    string? Currency,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int? FiscalYear,
    string? FiscalQuarter,
    string? MetricType,
    int? SourcePage,
    long? SourceChunkId,
    decimal? Confidence);

public interface IDocumentGraphRepository
{
    Task<DocumentPage?> FindPageAsync(int documentId, int pageNumber, CancellationToken cancellationToken);

    Task AddPageAsync(DocumentPage page, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentPage>> ListPagesAsync(int documentId, CancellationToken cancellationToken);

    Task<DocumentSection?> FindSectionAsync(long sectionId, CancellationToken cancellationToken);

    Task AddSectionAsync(DocumentSection section, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentSection>> ListSectionsAsync(int documentId, CancellationToken cancellationToken);

    Task<DocumentChunk?> FindChunkAsync(long chunkId, CancellationToken cancellationToken);

    Task<DocumentChunk?> FindChunkByIndexAsync(int documentId, int chunkIndex, CancellationToken cancellationToken);

    Task AddChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentChunk>> ListChunksAsync(int documentId, string? chunkType, int limit, int offset, CancellationToken cancellationToken);

    Task AddTableAsync(FinancialTable table, CancellationToken cancellationToken);

    Task<FinancialTable?> FindTableAsync(long tableId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinancialTable>> ListTablesAsync(int documentId, CancellationToken cancellationToken);

    Task AddMetricAsync(FinancialMetric metric, CancellationToken cancellationToken);

    Task<FinancialMetric?> FindMetricAsync(long metricId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinancialMetric>> ListMetricsAsync(
        int companyId,
        string? normalizedName,
        int? fiscalYear,
        string? fiscalQuarter,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        int limit,
        int offset,
        CancellationToken cancellationToken);
}
