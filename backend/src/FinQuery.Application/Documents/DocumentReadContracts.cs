namespace FinQuery.Application.Documents;

public sealed record DocumentListItem(
    int Id,
    int? CompanyId,
    string? CompanyName,
    string Title,
    string DocumentType,
    DateOnly? FilingDate,
    string ProcessingStatus,
    DateTime CreatedAt);

public sealed record DocumentResponse(
    int Id,
    int? CompanyId,
    string DocumentType,
    string Title,
    string? SourceUrl,
    string? SourceName,
    DateOnly? FilingDate,
    DateOnly? ReportingPeriodStart,
    DateOnly? ReportingPeriodEnd,
    int? FiscalYear,
    string? FiscalQuarter,
    string? Currency,
    string Language,
    string? FilePath,
    string? FileHash,
    int? PageCount,
    string ProcessingStatus,
    DateTime? ProcessingStartedAt,
    DateTime? ProcessingCompletedAt,
    string? ProcessingError,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IDocumentReadRepository
{
    Task<IReadOnlyList<DocumentListItem>> ListForUserAsync(int userId, int limit, CancellationToken cancellationToken);

    Task<DocumentResponse?> FindForUserAsync(int documentId, int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentResponse>> ListForCompanyAsync(
        int userId,
        int companyId,
        string? documentType,
        int? fiscalYear,
        int limit,
        int offset,
        CancellationToken cancellationToken);
}
