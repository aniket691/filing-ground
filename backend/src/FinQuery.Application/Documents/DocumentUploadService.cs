using FinQuery.Application.Companies;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Documents;

public sealed class DocumentUploadService(
    IDocumentWriteRepository documents,
    ICompanyRepository companies,
    IDocumentFileStore storage)
{
    public async Task<DocumentResponse> UploadAsync(
        int userId,
        string filename,
        byte[] data,
        int? companyId,
        string? documentType,
        string? sourceUrl,
        string? sourceName,
        CancellationToken cancellationToken)
    {
        if (data.Length == 0)
        {
            throw new ValidationError("Uploaded file is empty");
        }

        if (data.Length > UploadFormats.MaxBytes)
        {
            throw new ValidationError($"File exceeds upload limit ({UploadFormats.MaxBytes / (1024 * 1024)} MiB)");
        }

        UploadFormats.EnsureSupported(filename);
        var fileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
        var existing = await documents.FindByFileHashAsync(userId, companyId, fileHash, cancellationToken);
        if (existing is not null)
        {
            return DocumentMapper.ToResponse(existing);
        }

        var key = storage.Store(companyId, filename, data);
        if (companyId is not null && await companies.FindForUserAsync(companyId.Value, userId, cancellationToken) is null)
        {
            throw new NotFoundError("Company", companyId.Value);
        }

        var title = filename;
        var slash = title.LastIndexOf('/');
        title = slash >= 0 ? title[(slash + 1)..] : title;
        if (title.Length > 512)
        {
            title = title[..512];
        }

        var type = documentType is not null && DocumentTypes.All.Contains(documentType) ? documentType : "Other";
        var now = DateTime.UtcNow;
        var document = new Document
        {
            UserId = userId,
            CompanyId = companyId,
            DocumentType = type,
            Title = title,
            SourceUrl = sourceUrl,
            SourceName = string.IsNullOrEmpty(sourceName) ? null : sourceName.Length <= 255 ? sourceName : sourceName[..255],
            Language = "en",
            FilePath = key,
            FileHash = fileHash,
            ProcessingStatus = "uploaded",
            CreatedAt = now,
            UpdatedAt = now
        };
        await documents.AddAsync(document, cancellationToken);
        return DocumentMapper.ToResponse(document);
    }
}

public interface IDocumentWriteRepository
{
    Task<Document?> FindByFileHashAsync(int userId, int? companyId, string fileHash, CancellationToken cancellationToken);

    Task AddAsync(Document document, CancellationToken cancellationToken);
}
