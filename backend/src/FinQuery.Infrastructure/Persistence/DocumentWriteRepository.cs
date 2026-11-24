using FinQuery.Application.Documents;
using FinQuery.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Persistence;

public sealed class DocumentWriteRepository(FinQueryDbContext db) : IDocumentWriteRepository
{
    public Task<Document?> FindByFileHashAsync(
        int userId,
        int? companyId,
        string fileHash,
        CancellationToken cancellationToken)
    {
        var normalized = fileHash.ToLowerInvariant();
        return db.Documents.FirstOrDefaultAsync(
            document => document.UserId == userId
                && document.CompanyId == companyId
                && document.FileHash != null
                && document.FileHash.ToLower() == normalized,
            cancellationToken);
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }
}
