using System.Text.Json;
using FinQuery.Application.Companies;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Errors;
using FinQuery.Domain.Metrics;
using FinQuery.Domain.Tables;

namespace FinQuery.Application.Documents;

public sealed class DocumentGraphService(
    IDocumentGraphRepository graph,
    IDocumentReadRepository documents,
    IDocumentWriteRepository writes,
    ICompanyRepository companies)
{
    public async Task<DocumentResponse> CreateAsync(int userId, Document document, CancellationToken cancellationToken)
    {
        if (document.CompanyId is not null
            && await companies.FindForUserAsync(document.CompanyId.Value, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Company", document.CompanyId.Value);
        }

        if (!string.IsNullOrEmpty(document.FileHash))
        {
            var duplicate = await writes.FindByFileHashAsync(userId, document.CompanyId, document.FileHash, cancellationToken);
            if (duplicate is not null)
            {
                throw new ConflictError($"Document with file_hash '{document.FileHash}' already exists (id={duplicate.Id})");
            }
        }

        document.UserId = userId;
        var now = DateTime.UtcNow;
        document.CreatedAt = now;
        document.UpdatedAt = now;
        await writes.AddAsync(document, cancellationToken);
        return DocumentMapper.ToResponse(document);
    }

    public async Task<PageResponse> AddPageAsync(int userId, int documentId, PageWrite input, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        if (await graph.FindPageAsync(documentId, input.PageNumber, cancellationToken) is not null)
        {
            throw new ConflictError($"Page {input.PageNumber} already exists for document {documentId}");
        }

        var page = new DocumentPage
        {
            DocumentId = documentId,
            PageNumber = input.PageNumber,
            RawText = input.RawText,
            CleanedText = input.CleanedText,
            ExtractionMethod = input.ExtractionMethod,
            ExtractionMetadataJson = input.ExtractionMetadataJson,
            CreatedAt = DateTime.UtcNow
        };
        await graph.AddPageAsync(page, cancellationToken);
        return ToPage(page);
    }

    public async Task<IReadOnlyList<PageResponse>> ListPagesAsync(int userId, int documentId, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        var pages = await graph.ListPagesAsync(documentId, cancellationToken);
        return pages.Select(ToPage).ToList();
    }

    public async Task<SectionResponse> AddSectionAsync(int userId, int documentId, SectionWrite input, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        if (input.ParentSectionId is not null)
        {
            var parent = await graph.FindSectionAsync(input.ParentSectionId.Value, cancellationToken);
            if (parent is null || parent.DocumentId != documentId)
            {
                throw new ValidationError($"parent_section_id {input.ParentSectionId} does not belong to document {documentId}");
            }
        }

        var section = new DocumentSection
        {
            DocumentId = documentId,
            ParentSectionId = input.ParentSectionId,
            SectionTitle = input.SectionTitle,
            SectionType = input.SectionType,
            PageStart = input.PageStart,
            PageEnd = input.PageEnd,
            Text = input.Text,
            CreatedAt = DateTime.UtcNow
        };
        await graph.AddSectionAsync(section, cancellationToken);
        return ToSection(section);
    }

    public async Task<IReadOnlyList<SectionResponse>> ListSectionsAsync(int userId, int documentId, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        var sections = await graph.ListSectionsAsync(documentId, cancellationToken);
        return sections.Select(ToSection).ToList();
    }

    public async Task<ChunkResponse> AddChunkAsync(int userId, int documentId, ChunkWrite input, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        if (input.SectionId is not null)
        {
            var section = await graph.FindSectionAsync(input.SectionId.Value, cancellationToken);
            if (section is null || section.DocumentId != documentId)
            {
                throw new ValidationError($"section_id {input.SectionId} does not belong to document {documentId}");
            }
        }

        if (await graph.FindChunkByIndexAsync(documentId, input.ChunkIndex, cancellationToken) is not null)
        {
            throw new ConflictError($"chunk_index {input.ChunkIndex} already exists for document {documentId}");
        }

        var chunk = new DocumentChunk
        {
            DocumentId = documentId,
            SectionId = input.SectionId,
            ChunkIndex = input.ChunkIndex,
            ChunkType = input.ChunkType,
            Text = input.Text,
            TokenCount = input.TokenCount,
            PageStart = input.PageStart,
            PageEnd = input.PageEnd,
            MetadataJson = input.MetadataJson,
            CreatedAt = DateTime.UtcNow
        };
        await graph.AddChunkAsync(chunk, cancellationToken);
        return ToChunk(chunk);
    }

    public async Task<IReadOnlyList<ChunkResponse>> ListChunksAsync(
        int userId, int documentId, string? chunkType, int limit, int offset, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        var chunks = await graph.ListChunksAsync(
            documentId,
            string.IsNullOrEmpty(chunkType) ? null : chunkType,
            limit,
            Math.Max(0, offset),
            cancellationToken);
        return chunks.Select(ToChunk).ToList();
    }

    public async Task<TableResponse> AddTableAsync(int userId, int documentId, TableWrite input, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        if (input.SourceChunkId is not null)
        {
            var chunk = await graph.FindChunkAsync(input.SourceChunkId.Value, cancellationToken);
            if (chunk is null || chunk.DocumentId != documentId)
            {
                throw new ValidationError($"source_chunk_id {input.SourceChunkId} does not belong to document {documentId}");
            }
        }

        var now = DateTime.UtcNow;
        var table = new FinancialTable
        {
            DocumentId = documentId,
            SourceChunkId = input.SourceChunkId,
            PageNumber = input.PageNumber,
            Title = input.Title,
            HeadersJson = input.HeadersJson,
            Units = input.Units,
            Currency = input.Currency,
            ExtractionConfidence = input.ExtractionConfidence,
            CreatedAt = now,
            Rows = input.Rows.OrderBy(row => row.RowIndex).Select(row => new FinancialTableRow
            {
                RowIndex = row.RowIndex,
                RowLabel = row.RowLabel,
                CellsJson = row.CellsJson,
                CreatedAt = now
            }).ToList()
        };
        await graph.AddTableAsync(table, cancellationToken);
        return ToTable(table);
    }

    public async Task<IReadOnlyList<TableResponse>> ListTablesAsync(int userId, int documentId, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        var tables = await graph.ListTablesAsync(documentId, cancellationToken);
        return tables.Select(ToTable).ToList();
    }

    public async Task<TableResponse> GetTableAsync(int userId, int documentId, long tableId, CancellationToken cancellationToken)
    {
        await RequireDocumentAsync(userId, documentId, cancellationToken);
        var table = await graph.FindTableAsync(tableId, cancellationToken);
        if (table is null || table.DocumentId != documentId)
        {
            throw new NotFoundError("FinancialTable", tableId.ToString());
        }

        return ToTable(table);
    }

    public async Task<MetricResponse> CreateMetricAsync(int userId, int companyId, MetricWrite input, CancellationToken cancellationToken)
    {
        if (await companies.FindForUserAsync(companyId, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Company", companyId);
        }

        var document = await documents.FindForUserAsync(input.DocumentId, userId, cancellationToken);
        if (document is null)
        {
            throw new NotFoundError("Document", input.DocumentId);
        }

        if (document.CompanyId != companyId)
        {
            throw new ValidationError($"Document {input.DocumentId} does not belong to company {companyId}");
        }

        if (input.SourceChunkId is not null)
        {
            var chunk = await graph.FindChunkAsync(input.SourceChunkId.Value, cancellationToken);
            if (chunk is null || chunk.DocumentId != document.Id)
            {
                throw new ValidationError($"source_chunk_id {input.SourceChunkId} does not belong to document {document.Id}");
            }
        }

        var metric = new FinancialMetric
        {
            CompanyId = companyId,
            DocumentId = input.DocumentId,
            MetricName = input.MetricName.Trim(),
            NormalizedMetricName = MetricNames.Normalize(input.MetricName),
            Value = input.Value,
            Unit = input.Unit,
            Currency = input.Currency,
            PeriodStart = input.PeriodStart,
            PeriodEnd = input.PeriodEnd,
            FiscalYear = input.FiscalYear,
            FiscalQuarter = input.FiscalQuarter,
            MetricType = input.MetricType,
            SourcePage = input.SourcePage,
            SourceChunkId = input.SourceChunkId,
            Confidence = input.Confidence,
            CreatedAt = DateTime.UtcNow
        };
        await graph.AddMetricAsync(metric, cancellationToken);
        return ToMetric(metric);
    }

    public async Task<MetricResponse> GetMetricAsync(int userId, int companyId, long metricId, CancellationToken cancellationToken)
    {
        if (await companies.FindForUserAsync(companyId, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Company", companyId);
        }

        var metric = await graph.FindMetricAsync(metricId, cancellationToken);
        if (metric is null || metric.CompanyId != companyId)
        {
            throw new NotFoundError("FinancialMetric", metricId.ToString());
        }

        return ToMetric(metric);
    }

    public async Task<IReadOnlyList<MetricResponse>> ListMetricsAsync(
        int userId,
        int companyId,
        string? metricName,
        int? fiscalYear,
        string? fiscalQuarter,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        if (await companies.FindForUserAsync(companyId, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Company", companyId);
        }

        var normalized = string.IsNullOrEmpty(metricName) ? null : MetricNames.Normalize(metricName);
        if (string.IsNullOrEmpty(normalized))
        {
            normalized = null;
        }

        var rows = await graph.ListMetricsAsync(
            companyId,
            normalized,
            fiscalYear,
            string.IsNullOrEmpty(fiscalQuarter) ? null : fiscalQuarter,
            periodStart,
            periodEnd,
            Math.Clamp(limit, 1, 500),
            Math.Max(0, offset),
            cancellationToken);
        return rows.Select(ToMetric).ToList();
    }

    private async Task RequireDocumentAsync(int userId, int documentId, CancellationToken cancellationToken)
    {
        if (await documents.FindForUserAsync(documentId, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Document", documentId);
        }
    }

    private static PageResponse ToPage(DocumentPage page) => new(
        page.Id, page.DocumentId, page.PageNumber, page.RawText, page.CleanedText, page.ExtractionMethod,
        ParseJson(page.ExtractionMetadataJson, "{}"), page.CreatedAt);

    private static SectionResponse ToSection(DocumentSection section) => new(
        section.Id, section.DocumentId, section.ParentSectionId, section.SectionTitle, section.SectionType,
        section.PageStart, section.PageEnd, section.Text, section.CreatedAt);

    private static ChunkResponse ToChunk(DocumentChunk chunk) => new(
        chunk.Id, chunk.DocumentId, chunk.SectionId, chunk.ChunkIndex, chunk.ChunkType, chunk.Text,
        chunk.TokenCount, chunk.PageStart, chunk.PageEnd, ParseJson(chunk.MetadataJson, "{}"), chunk.CreatedAt);

    private static TableResponse ToTable(FinancialTable table) => new(
        table.Id, table.DocumentId, table.SourceChunkId, table.PageNumber, table.Title,
        ParseJson(table.HeadersJson, "[]"), table.Units, table.Currency, table.ExtractionConfidence, table.CreatedAt,
        table.Rows.OrderBy(row => row.RowIndex).Select(row => new TableRowResponse(
            row.Id, row.TableId, row.RowIndex, row.RowLabel, ParseJson(row.CellsJson, "[]"), row.CreatedAt)).ToList());

    private static MetricResponse ToMetric(FinancialMetric metric) => new(
        metric.Id, metric.CompanyId, metric.DocumentId, metric.MetricName, metric.NormalizedMetricName, metric.Value,
        metric.Unit, metric.Currency, metric.PeriodStart, metric.PeriodEnd, metric.FiscalYear, metric.FiscalQuarter,
        metric.MetricType, metric.SourcePage, metric.SourceChunkId, metric.Confidence, metric.CreatedAt);

    private static JsonElement ParseJson(string? json, string fallback)
    {
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(json) ? fallback : json);
        return document.RootElement.Clone();
    }
}
