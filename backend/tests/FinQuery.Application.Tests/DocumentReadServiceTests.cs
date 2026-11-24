using FinQuery.Application.Companies;
using FinQuery.Application.Documents;
using FinQuery.Domain.Companies;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Tests;

public class DocumentReadServiceTests
{
    [Fact]
    public async Task Get_OtherUserOrMissing_IsNotFound()
    {
        var documents = new MemoryDocuments();
        documents.Details.Add(Detail(5));
        var service = new DocumentReadService(documents, new MemoryCompanies());

        var error = await Assert.ThrowsAsync<NotFoundError>(() => service.GetAsync(2, 5, CancellationToken.None));
        Assert.Equal("Document with id 5 not found", error.Message);
        await Assert.ThrowsAsync<NotFoundError>(() => service.GetAsync(1, 99, CancellationToken.None));
    }

    [Fact]
    public async Task CompanyList_MissingCompany_IsNotFound()
    {
        var service = new DocumentReadService(new MemoryDocuments(), new MemoryCompanies());
        var error = await Assert.ThrowsAsync<NotFoundError>(() =>
            service.ListForCompanyAsync(1, 44, null, null, 50, 0, CancellationToken.None));
        Assert.Equal("Company with id 44 not found", error.Message);
    }

    private static DocumentResponse Detail(int id) =>
        new(id, null, "10-K", "Title", null, null, null, null, null, null, null, null, "en", null, null, null, "completed", null, null, null, DateTime.UtcNow, DateTime.UtcNow);

    private sealed class MemoryCompanies : ICompanyRepository
    {
        public List<Company> Items { get; } = [];

        public Task<Company?> FindForUserAsync(int companyId, int userId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(company => company.Id == companyId && company.UserId == userId));

        public Task<Company?> FindByTickerAndExchangeAsync(int userId, string ticker, string? exchange, CancellationToken cancellationToken) =>
            Task.FromResult<Company?>(null);

        public Task<Company?> FindByLegalNameAsync(int userId, string legalName, CancellationToken cancellationToken) =>
            Task.FromResult<Company?>(null);

        public Task<(IReadOnlyList<Company> Items, int Total)> SearchAsync(int userId, string? query, int limit, int offset, CancellationToken cancellationToken) =>
            Task.FromResult(((IReadOnlyList<Company>)Array.Empty<Company>(), 0));

        public Task AddAsync(Company company, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryDocuments : IDocumentReadRepository
    {
        public List<DocumentListItem> Rows { get; } = [];

        public List<DocumentResponse> Details { get; } = [];

        public int VisibleUserId { get; set; } = 1;

        public Task<IReadOnlyList<DocumentListItem>> ListForUserAsync(int userId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentListItem>>(Rows.Take(limit).ToList());

        public Task<DocumentResponse?> FindForUserAsync(int documentId, int userId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == VisibleUserId ? Details.FirstOrDefault(row => row.Id == documentId) : null);

        public Task<IReadOnlyList<DocumentResponse>> ListForCompanyAsync(
            int userId, int companyId, string? documentType, int? fiscalYear, int limit, int offset, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentResponse>>(Details);
    }
}
