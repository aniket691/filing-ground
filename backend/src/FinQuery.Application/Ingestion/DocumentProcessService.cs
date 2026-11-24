using FinQuery.Application.Documents;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Ingestion;

public interface IDocumentProcessRepository
{
    Task<Document?> FindForUserAsync(int documentId, int userId, CancellationToken cancellationToken);

    Task<Document?> FindAsync(int documentId, CancellationToken cancellationToken);

    Task SaveStatusAsync(Document document, CancellationToken cancellationToken);

    Task ReplaceDerivedRowsAsync(Document document, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);
}

public sealed class DocumentProcessService(IDocumentProcessRepository documents, IIngestionWorkQueue queue)
{
    public async Task<DocumentStatusResponse> QueueAsync(int userId, int documentId, bool force, CancellationToken cancellationToken)
    {
        var document = await documents.FindForUserAsync(documentId, userId, cancellationToken);
        if (document is null)
        {
            throw new NotFoundError("Document", documentId);
        }

        if (!force && document.ProcessingStatus == "processing")
        {
            throw new ConflictError($"Document {documentId} is already being processed");
        }

        document.ProcessingStatus = "queued";
        await documents.SaveStatusAsync(document, cancellationToken);
        queue.Enqueue(documentId, force);
        return ToStatus(document);
    }

    public async Task<DocumentStatusResponse> GetStatusAsync(int userId, int documentId, CancellationToken cancellationToken)
    {
        var document = await documents.FindForUserAsync(documentId, userId, cancellationToken);
        if (document is null)
        {
            throw new NotFoundError("Document", documentId);
        }

        return ToStatus(document);
    }

    public static DocumentStatusResponse ToStatus(Document document) => new(
        document.Id,
        document.ProcessingStatus,
        document.PageCount,
        document.ProcessingStartedAt,
        document.ProcessingCompletedAt,
        document.ProcessingError);
}
