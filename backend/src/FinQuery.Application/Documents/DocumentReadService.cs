using FinQuery.Application.Companies;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Documents;

public sealed class DocumentReadService(IDocumentReadRepository documents, ICompanyRepository companies)
{
    public Task<IReadOnlyList<DocumentListItem>> ListAsync(int userId, int limit, CancellationToken cancellationToken) =>
        documents.ListForUserAsync(userId, limit, cancellationToken);

    public async Task<DocumentResponse> GetAsync(int userId, int documentId, CancellationToken cancellationToken)
    {
        var document = await documents.FindForUserAsync(documentId, userId, cancellationToken);
        if (document is null)
        {
            throw new NotFoundError("Document", documentId);
        }

        return document;
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
        if (await companies.FindForUserAsync(companyId, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Company", companyId);
        }

        return await documents.ListForCompanyAsync(
            userId,
            companyId,
            string.IsNullOrEmpty(documentType) ? null : documentType,
            fiscalYear,
            Math.Clamp(limit, 1, 200),
            Math.Max(0, offset),
            cancellationToken);
    }
}
