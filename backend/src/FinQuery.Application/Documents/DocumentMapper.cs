using FinQuery.Domain.Documents;

namespace FinQuery.Application.Documents;

public static class DocumentMapper
{
    public static DocumentResponse ToResponse(Document document) => new(
        document.Id,
        document.CompanyId,
        document.DocumentType,
        document.Title,
        document.SourceUrl,
        document.SourceName,
        document.FilingDate,
        document.ReportingPeriodStart,
        document.ReportingPeriodEnd,
        document.FiscalYear,
        document.FiscalQuarter,
        document.Currency,
        document.Language,
        document.FilePath,
        document.FileHash,
        document.PageCount,
        document.ProcessingStatus,
        document.ProcessingStartedAt,
        document.ProcessingCompletedAt,
        document.ProcessingError,
        document.CreatedAt,
        document.UpdatedAt);
}
