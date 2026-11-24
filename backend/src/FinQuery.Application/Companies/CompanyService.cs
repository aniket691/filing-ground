using FinQuery.Domain.Companies;
using FinQuery.Domain.Errors;

namespace FinQuery.Application.Companies;

public sealed class CompanyService(ICompanyRepository companies)
{
    public async Task<CompanyResponse> CreateAsync(int userId, CompanyWrite input, CancellationToken cancellationToken)
    {
        var legalName = input.LegalName.Trim();
        var ticker = string.IsNullOrEmpty(input.Ticker) ? null : input.Ticker.Trim().ToUpperInvariant();
        if (ticker is not null && ticker.Length == 0)
        {
            ticker = null;
        }

        if (ticker is not null)
        {
            var byTicker = await companies.FindByTickerAndExchangeAsync(userId, ticker, input.Exchange, cancellationToken);
            if (byTicker is not null)
            {
                throw new ConflictError($"Company with ticker '{ticker}' already exists (id={byTicker.Id})");
            }
        }

        var byName = await companies.FindByLegalNameAsync(userId, legalName, cancellationToken);
        if (byName is not null)
        {
            throw new ConflictError($"Company with legal_name '{legalName}' already exists (id={byName.Id})");
        }

        var now = DateTime.UtcNow;
        var company = new Company
        {
            UserId = userId,
            LegalName = legalName,
            DisplayName = string.IsNullOrEmpty(input.DisplayName) ? legalName : input.DisplayName.Trim(),
            Ticker = ticker,
            Exchange = input.Exchange,
            Country = string.IsNullOrEmpty(input.Country) ? null : input.Country.ToUpperInvariant(),
            Industry = input.Industry,
            Sector = input.Sector,
            CreatedAt = now,
            UpdatedAt = now
        };
        await companies.AddAsync(company, cancellationToken);
        return ToResponse(company);
    }

    public async Task<CompanyResponse> GetAsync(int userId, int companyId, CancellationToken cancellationToken)
    {
        var company = await companies.FindForUserAsync(companyId, userId, cancellationToken);
        if (company is null)
        {
            throw new NotFoundError("Company", companyId);
        }

        return ToResponse(company);
    }

    public async Task<CompanyListResponse> ListAsync(
        int userId,
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        var (items, total) = await companies.SearchAsync(userId, query, limit, offset, cancellationToken);
        return new CompanyListResponse(items.Select(ToResponse).ToList(), total);
    }

    public async Task<CompanyResponse> UpdateAsync(
        int userId,
        int companyId,
        CompanyPatch patch,
        CancellationToken cancellationToken)
    {
        var company = await companies.FindForUserAsync(companyId, userId, cancellationToken);
        if (company is null)
        {
            throw new NotFoundError("Company", companyId);
        }

        var changed = false;
        if (patch.DisplayNameSet)
        {
            company.DisplayName = patch.DisplayName;
            changed = true;
        }

        if (patch.CountrySet)
        {
            company.Country = string.IsNullOrEmpty(patch.Country) ? null : patch.Country.ToUpperInvariant();
            changed = true;
        }

        if (patch.IndustrySet)
        {
            company.Industry = patch.Industry;
            changed = true;
        }

        if (patch.SectorSet)
        {
            company.Sector = patch.Sector;
            changed = true;
        }

        if (changed)
        {
            company.UpdatedAt = DateTime.UtcNow;
            await companies.SaveAsync(cancellationToken);
        }

        return ToResponse(company);
    }

    public static CompanyResponse ToResponse(Company company) => new(
        company.Id,
        company.LegalName,
        company.DisplayName,
        company.Ticker,
        company.Exchange,
        company.Country,
        company.Industry,
        company.Sector,
        company.CreatedAt,
        company.UpdatedAt);
}
