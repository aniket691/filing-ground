using FinQuery.Application.Ingestion;
using FinQuery.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Persistence;

public sealed class DocumentProcessRepository(FinQueryDbContext db) : IDocumentProcessRepository
{
    public Task<Document?> FindForUserAsync(int documentId, int userId, CancellationToken cancellationToken) =>
        db.Documents.FirstOrDefaultAsync(document => document.Id == documentId && document.UserId == userId, cancellationToken);

    public Task<Document?> FindAsync(int documentId, CancellationToken cancellationToken) =>
        db.Documents.FirstOrDefaultAsync(document => document.Id == documentId, cancellationToken);

    public Task SaveStatusAsync(Document document, CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public Task ReplaceDerivedRowsAsync(Document document, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
