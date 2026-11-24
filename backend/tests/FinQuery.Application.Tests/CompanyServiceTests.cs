using FinQuery.Application.Companies;
using FinQuery.Domain.Companies;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Tests;

public class CompanyServiceTests
{
    [Fact]
    public async Task Create_NormalizesTickerAndCountry_AndDefaultsDisplayName()
    {
        var repo = new FakeCompanies();
        var service = new CompanyService(repo);

        var created = await service.CreateAsync(7, new CompanyWrite("  Acme Corp  ", null, "acme", "nyse", "us", null, null), CancellationToken.None);

        Assert.Equal("Acme Corp", created.LegalName);
        Assert.Equal("Acme Corp", created.DisplayName);
        Assert.Equal("ACME", created.Ticker);
        Assert.Equal("nyse", created.Exchange);
        Assert.Equal("US", created.Country);
        Assert.Equal(7, repo.Items[0].UserId);
    }

    [Fact]
    public async Task Create_DuplicateLegalName_IsCaseInsensitive()
    {
        var service = new CompanyService(new FakeCompanies());
        await service.CreateAsync(1, new CompanyWrite("Acme Corp", null, "ACME", "NYSE", null, null, null), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ConflictError>(() =>
            service.CreateAsync(1, new CompanyWrite("acme corp", null, null, null, null, null, null), CancellationToken.None));

        Assert.Contains("legal_name 'acme corp'", error.Message);
    }

    [Fact]
    public async Task Create_SameTickerOnAnotherExchange_IsAllowed()
    {
        var service = new CompanyService(new FakeCompanies());
        var first = await service.CreateAsync(1, new CompanyWrite("Acme Corp", null, "ACME", "NYSE", null, null, null), CancellationToken.None);
        var second = await service.CreateAsync(1, new CompanyWrite("Acme Corp Japan", null, "acme", "TSE", null, null, null), CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Get_MissingOrOtherUser_IsNotFound()
    {
        var service = new CompanyService(new FakeCompanies());
        var created = await service.CreateAsync(1, new CompanyWrite("Acme Corp", null, null, null, null, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundError>(() => service.GetAsync(1, 99999, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundError>(() => service.GetAsync(2, created.Id, CancellationToken.None));
    }

    private sealed class FakeCompanies : ICompanyRepository
    {
        public List<Company> Items { get; } = [];

        public Task<Company?> FindForUserAsync(int companyId, int userId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(company => company.Id == companyId && company.UserId == userId));

        public Task<Company?> FindByTickerAndExchangeAsync(int userId, string ticker, string? exchange, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(company =>
                company.UserId == userId
                && string.Equals(company.Ticker, ticker, StringComparison.OrdinalIgnoreCase)
                && string.Equals(company.Exchange, exchange, StringComparison.OrdinalIgnoreCase)));

        public Task<Company?> FindByLegalNameAsync(int userId, string legalName, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(company =>
                company.UserId == userId
                && string.Equals(company.LegalName, legalName.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<(IReadOnlyList<Company> Items, int Total)> SearchAsync(
            int userId, string? query, int limit, int offset, CancellationToken cancellationToken)
        {
            IEnumerable<Company> rows = Items.Where(company => company.UserId == userId).OrderBy(company => company.Id);
            if (!string.IsNullOrEmpty(query))
            {
                var term = query.Trim().ToLowerInvariant();
                rows = rows.Where(company =>
                    company.LegalName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (company.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (company.Ticker?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var list = rows.ToList();
            return Task.FromResult(((IReadOnlyList<Company>)list.Skip(offset).Take(limit).ToList(), list.Count));
        }

        public Task AddAsync(Company company, CancellationToken cancellationToken)
        {
            company.Id = Items.Count + 1;
            Items.Add(company);
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
