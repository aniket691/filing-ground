using FinQuery.Application.Documents;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Persistence;

public sealed class DocumentReadRepository(FinQueryDbContext db) : IDocumentReadRepository
{
    public async Task<IReadOnlyList<DocumentListItem>> ListForUserAsync(
        int userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from document in db.Documents
            join company in db.Companies on document.CompanyId equals company.Id into matched
            from company in matched.DefaultIfEmpty()
            where document.UserId == userId
            orderby document.Id descending
            select new { document, company }
        ).Take(limit).ToListAsync(cancellationToken);

        return rows.Select(row => new DocumentListItem(
            row.document.Id,
            row.document.CompanyId,
            row.company is null
                ? null
                : string.IsNullOrEmpty(row.company.DisplayName) ? row.company.LegalName : row.company.DisplayName,
            row.document.Title,
            row.document.DocumentType,
            row.document.FilingDate,
            row.document.ProcessingStatus,
            row.document.CreatedAt)).ToList();
    }

    public async Task<DocumentResponse?> FindForUserAsync(
        int documentId,
        int userId,
        CancellationToken cancellationToken)
    {
        var document = await db.Documents.FirstOrDefaultAsync(
            row => row.Id == documentId && row.UserId == userId,
            cancellationToken);
        return document is null ? null : DocumentMapper.ToResponse(document);
    }

    public async Task<IReadOnlyList<DocumentResponse>> ListForCompanyAsync(
        int userId,
        int companyId,
        string? documentType,
        int? fiscalYear,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var query = db.Documents.Where(document => document.UserId == userId && document.CompanyId == companyId);
        if (documentType is not null)
        {
            query = query.Where(document => document.DocumentType == documentType);
        }

        if (fiscalYear is not null)
        {
            query = query.Where(document => document.FiscalYear == fiscalYear);
        }

        var rows = await query
            .OrderBy(document => document.FilingDate == null)
            .ThenByDescending(document => document.FilingDate)
            .ThenByDescending(document => document.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(DocumentMapper.ToResponse).ToList();
    }
}
