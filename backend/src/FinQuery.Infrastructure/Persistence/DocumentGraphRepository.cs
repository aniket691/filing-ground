using FinQuery.Application.Documents;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Metrics;
using FinQuery.Domain.Tables;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Persistence;

public sealed class DocumentGraphRepository(FinQueryDbContext db) : IDocumentGraphRepository
{
    public Task<DocumentPage?> FindPageAsync(int documentId, int pageNumber, CancellationToken cancellationToken) =>
        db.DocumentPages.FirstOrDefaultAsync(page => page.DocumentId == documentId && page.PageNumber == pageNumber, cancellationToken);

    public async Task AddPageAsync(DocumentPage page, CancellationToken cancellationToken)
    {
        db.DocumentPages.Add(page);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentPage>> ListPagesAsync(int documentId, CancellationToken cancellationToken) =>
        await db.DocumentPages.Where(page => page.DocumentId == documentId).OrderBy(page => page.PageNumber).ToListAsync(cancellationToken);

    public Task<DocumentSection?> FindSectionAsync(long sectionId, CancellationToken cancellationToken) =>
        db.DocumentSections.FirstOrDefaultAsync(section => section.Id == sectionId, cancellationToken);

    public async Task AddSectionAsync(DocumentSection section, CancellationToken cancellationToken)
    {
        db.DocumentSections.Add(section);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentSection>> ListSectionsAsync(int documentId, CancellationToken cancellationToken) =>
        await db.DocumentSections.Where(section => section.DocumentId == documentId)
            .OrderBy(section => section.PageStart == null)
            .ThenBy(section => section.PageStart)
            .ThenBy(section => section.Id)
            .ToListAsync(cancellationToken);

    public Task<DocumentChunk?> FindChunkAsync(long chunkId, CancellationToken cancellationToken) =>
        db.DocumentChunks.FirstOrDefaultAsync(chunk => chunk.Id == chunkId, cancellationToken);

    public Task<DocumentChunk?> FindChunkByIndexAsync(int documentId, int chunkIndex, CancellationToken cancellationToken) =>
        db.DocumentChunks.FirstOrDefaultAsync(chunk => chunk.DocumentId == documentId && chunk.ChunkIndex == chunkIndex, cancellationToken);

    public async Task AddChunkAsync(DocumentChunk chunk, CancellationToken cancellationToken)
    {
        db.DocumentChunks.Add(chunk);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunk>> ListChunksAsync(
        int documentId, string? chunkType, int limit, int offset, CancellationToken cancellationToken)
    {
        var query = db.DocumentChunks.Where(chunk => chunk.DocumentId == documentId);
        if (chunkType is not null)
        {
            query = query.Where(chunk => chunk.ChunkType == chunkType);
        }

        return await query.OrderBy(chunk => chunk.ChunkIndex).Skip(offset).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task AddTableAsync(FinancialTable table, CancellationToken cancellationToken)
    {
        var rows = table.Rows.ToList();
        table.Rows = [];
        db.FinancialTables.Add(table);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.TableId = table.Id;
            db.FinancialTableRows.Add(row);
        }

        await db.SaveChangesAsync(cancellationToken);
        table.Rows = rows;
    }

    public async Task<FinancialTable?> FindTableAsync(long tableId, CancellationToken cancellationToken)
    {
        var table = await db.FinancialTables.FirstOrDefaultAsync(item => item.Id == tableId, cancellationToken);
        if (table is null)
        {
            return null;
        }

        table.Rows = await db.FinancialTableRows.Where(row => row.TableId == tableId).OrderBy(row => row.RowIndex).ToListAsync(cancellationToken);
        return table;
    }

    public async Task<IReadOnlyList<FinancialTable>> ListTablesAsync(int documentId, CancellationToken cancellationToken)
    {
        var tables = await db.FinancialTables.Where(table => table.DocumentId == documentId)
            .OrderBy(table => table.PageNumber == null)
            .ThenBy(table => table.PageNumber)
            .ThenBy(table => table.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (tables.Count == 0)
        {
            return tables;
        }

        var ids = tables.Select(table => table.Id).ToArray();
        var rows = await db.FinancialTableRows.Where(row => ids.Contains(row.TableId)).OrderBy(row => row.RowIndex).ToListAsync(cancellationToken);
        foreach (var table in tables)
        {
            table.Rows = rows.Where(row => row.TableId == table.Id).ToList();
        }

        return tables;
    }

    public async Task AddMetricAsync(FinancialMetric metric, CancellationToken cancellationToken)
    {
        db.FinancialMetrics.Add(metric);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<FinancialMetric?> FindMetricAsync(long metricId, CancellationToken cancellationToken) =>
        db.FinancialMetrics.FirstOrDefaultAsync(metric => metric.Id == metricId, cancellationToken);

    public async Task<IReadOnlyList<FinancialMetric>> ListMetricsAsync(
        int companyId,
        string? normalizedName,
        int? fiscalYear,
        string? fiscalQuarter,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = db.FinancialMetrics.Where(metric => metric.CompanyId == companyId);
        if (!string.IsNullOrEmpty(normalizedName))
        {
            query = query.Where(metric => metric.NormalizedMetricName == normalizedName);
        }

        if (fiscalYear is not null)
        {
            query = query.Where(metric => metric.FiscalYear == fiscalYear);
        }

        if (fiscalQuarter is not null)
        {
            query = query.Where(metric => metric.FiscalQuarter == fiscalQuarter);
        }

        if (periodStart is not null)
        {
            var start = periodStart.Value;
            query = query.Where(metric =>
                (metric.PeriodEnd != null && metric.PeriodEnd >= start)
                || (metric.PeriodStart != null && metric.PeriodStart >= start)
                || metric.PeriodEnd == null);
        }

        if (periodEnd is not null)
        {
            var end = periodEnd.Value;
            query = query.Where(metric => metric.PeriodStart == null || metric.PeriodStart <= end);
        }

        return await query
            .OrderBy(metric => metric.NormalizedMetricName)
            .ThenBy(metric => metric.PeriodStart == null)
            .ThenBy(metric => metric.PeriodStart)
            .ThenBy(metric => metric.FiscalYear == null)
            .ThenBy(metric => metric.FiscalYear)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
