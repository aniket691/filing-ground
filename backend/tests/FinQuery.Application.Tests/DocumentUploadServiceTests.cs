using FinQuery.Application.Companies;
using FinQuery.Application.Documents;
using FinQuery.Domain.Companies;
using FinQuery.Domain.Documents;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Tests;

public class DocumentUploadServiceTests
{
    [Fact]
    public async Task Upload_StoresPdf_AndReturnsTheExistingRowForTheSameHash()
    {
        var documents = new MemoryDocuments();
        var store = new MemoryStore();
        var service = new DocumentUploadService(documents, new MemoryCompanies(7), store);
        var bytes = "%PDF-1.4 test"u8.ToArray();

        var first = await service.UploadAsync(3, "reports/annual report.pdf", bytes, 7, "10-K", null, null, CancellationToken.None);
        var second = await service.UploadAsync(3, "other.pdf", bytes, 7, "10-Q", null, null, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("annual report.pdf", first.Title);
        Assert.Equal("uploaded", first.ProcessingStatus);
        Assert.Equal("10-K", first.DocumentType);
        Assert.Equal(1, store.Calls);
        Assert.StartsWith("companies/7/", first.FilePath);
        Assert.EndsWith("/annual report.pdf", first.FilePath);
        Assert.Equal(64, first.FileHash!.Length);
    }

    [Fact]
    public async Task SameBytesForAnotherCompany_AreStoredAgain()
    {
        var documents = new MemoryDocuments();
        var store = new MemoryStore();
        var companies = new MemoryCompanies(7);
        companies.Ids.Add(8);
        var service = new DocumentUploadService(documents, companies, store);
        var bytes = "hello"u8.ToArray();

        var first = await service.UploadAsync(3, "note.txt", bytes, 7, null, null, null, CancellationToken.None);
        var second = await service.UploadAsync(3, "note.txt", bytes, 8, null, null, null, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.Calls);
        Assert.Contains("/8/", second.FilePath);
    }

    [Fact]
    public async Task UnknownType_BecomesOther_AndLongSourceNameIsCut()
    {
        var service = new DocumentUploadService(new MemoryDocuments(), new MemoryCompanies(1), new MemoryStore());
        var created = await service.UploadAsync(
            3, "page.html", "<p>x</p>"u8.ToArray(), 1, "Form 999-X", null, new string('n', 300), CancellationToken.None);

        Assert.Equal("Other", created.DocumentType);
        Assert.Equal(255, created.SourceName!.Length);
    }

    [Fact]
    public async Task EmptyUnsupportedAndOversized_Are422Messages()
    {
        var service = new DocumentUploadService(new MemoryDocuments(), new MemoryCompanies(1), new MemoryStore());

        Assert.Equal("Uploaded file is empty",
            (await Assert.ThrowsAsync<ValidationError>(() => service.UploadAsync(1, "empty.pdf", [], 1, null, null, null, CancellationToken.None))).Message);
        Assert.Equal(
            "Unsupported file format 'exe'. Supported: ['htm', 'html', 'md', 'pdf', 'text', 'txt']",
            (await Assert.ThrowsAsync<ValidationError>(() => service.UploadAsync(1, "payload.exe", [1], 1, null, null, null, CancellationToken.None))).Message);
        var oversized = new byte[UploadFormats.MaxBytes + 1];
        Assert.Equal("File exceeds upload limit (25 MiB)",
            (await Assert.ThrowsAsync<ValidationError>(() => service.UploadAsync(1, "big.pdf", oversized, 1, null, null, null, CancellationToken.None))).Message);
    }

    [Fact]
    public async Task MissingCompany_Is404_AfterTheFileIsStored()
    {
        var store = new MemoryStore();
        var service = new DocumentUploadService(new MemoryDocuments(), new MemoryCompanies(1), store);

        var error = await Assert.ThrowsAsync<NotFoundError>(() =>
            service.UploadAsync(3, "report.pdf", "%PDF"u8.ToArray(), 99, "10-K", null, null, CancellationToken.None));

        Assert.Equal("Company with id 99 not found", error.Message);
        Assert.Equal(1, store.Calls);
    }

    private sealed class MemoryStore : IDocumentFileStore
    {
        public int Calls { get; private set; }

        public string Store(int? companyId, string filename, byte[] data)
        {
            Calls++;
            return DocumentStorageKeys.Build(companyId, filename, data);
        }

        public byte[] Read(string key) => [];
    }

    private sealed class MemoryCompanies(int id) : ICompanyRepository
    {
        public HashSet<int> Ids { get; } = [id];

        public Task<Company?> FindForUserAsync(int companyId, int userId, CancellationToken cancellationToken) =>
            Task.FromResult(Ids.Contains(companyId) ? new Company { Id = companyId, UserId = userId, LegalName = "Co" } : null);

        public Task<Company?> FindByTickerAndExchangeAsync(int userId, string ticker, string? exchange, CancellationToken cancellationToken) =>
            Task.FromResult<Company?>(null);

        public Task<Company?> FindByLegalNameAsync(int userId, string legalName, CancellationToken cancellationToken) =>
            Task.FromResult<Company?>(null);

        public Task<(IReadOnlyList<Company> Items, int Total)> SearchAsync(int userId, string? query, int limit, int offset, CancellationToken cancellationToken) =>
            Task.FromResult(((IReadOnlyList<Company>)Array.Empty<Company>(), 0));

        public Task AddAsync(Company company, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryDocuments : IDocumentWriteRepository
    {
        public List<Document> Items { get; } = [];

        public Task<Document?> FindByFileHashAsync(int userId, int? companyId, string fileHash, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(document =>
                document.UserId == userId
                && document.CompanyId == companyId
                && string.Equals(document.FileHash, fileHash, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Document document, CancellationToken cancellationToken)
        {
            document.Id = Items.Count + 1;
            Items.Add(document);
            return Task.CompletedTask;
        }
    }
}
