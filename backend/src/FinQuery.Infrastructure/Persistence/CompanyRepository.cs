using FinQuery.Application.Companies;
using FinQuery.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Persistence;

public sealed class CompanyRepository(FinQueryDbContext db) : ICompanyRepository
{
    public Task<Company?> FindForUserAsync(int companyId, int userId, CancellationToken cancellationToken)
    {
        return db.Companies.FirstOrDefaultAsync(
            company => company.Id == companyId && company.UserId == userId,
            cancellationToken);
    }

    public Task<Company?> FindByTickerAndExchangeAsync(
        int userId,
        string ticker,
        string? exchange,
        CancellationToken cancellationToken)
    {
        var normalizedTicker = ticker.ToLowerInvariant();
        var query = db.Companies.Where(company =>
            company.UserId == userId
            && company.Ticker != null
            && company.Ticker.ToLower() == normalizedTicker);
        if (exchange is null)
        {
            query = query.Where(company => company.Exchange == null);
        }
        else
        {
            var normalizedExchange = exchange.ToLowerInvariant();
            query = query.Where(company =>
                company.Exchange != null && company.Exchange.ToLower() == normalizedExchange);
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Company?> FindByLegalNameAsync(int userId, string legalName, CancellationToken cancellationToken)
    {
        var normalized = legalName.Trim().ToLowerInvariant();
        return db.Companies.FirstOrDefaultAsync(
            company => company.UserId == userId && company.LegalName.ToLower() == normalized,
            cancellationToken);
    }

    public async Task<(IReadOnlyList<Company> Items, int Total)> SearchAsync(
        int userId,
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var rows = db.Companies.Where(company => company.UserId == userId);
        if (!string.IsNullOrEmpty(query))
        {
            var pattern = $"%{query.Trim().ToLowerInvariant()}%";
            rows = rows.Where(company =>
                EF.Functions.Like(company.LegalName.ToLower(), pattern)
                || (company.DisplayName != null && EF.Functions.Like(company.DisplayName.ToLower(), pattern))
                || (company.Ticker != null && EF.Functions.Like(company.Ticker.ToLower(), pattern)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(company => company.Id).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task AddAsync(Company company, CancellationToken cancellationToken)
    {
        db.Companies.Add(company);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
