using FinQuery.Domain.Companies;

namespace FinQuery.Application.Companies;

public sealed record CompanyWrite(
    string LegalName,
    string? DisplayName,
    string? Ticker,
    string? Exchange,
    string? Country,
    string? Industry,
    string? Sector);

public sealed class CompanyPatch
{
    public bool DisplayNameSet { get; init; }

    public string? DisplayName { get; init; }

    public bool CountrySet { get; init; }

    public string? Country { get; init; }

    public bool IndustrySet { get; init; }

    public string? Industry { get; init; }

    public bool SectorSet { get; init; }

    public string? Sector { get; init; }
}

public sealed record CompanyResponse(
    int Id,
    string LegalName,
    string? DisplayName,
    string? Ticker,
    string? Exchange,
    string? Country,
    string? Industry,
    string? Sector,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CompanyListResponse(IReadOnlyList<CompanyResponse> Items, int Total);

public interface ICompanyRepository
{
    Task<Company?> FindForUserAsync(int companyId, int userId, CancellationToken cancellationToken);

    Task<Company?> FindByTickerAndExchangeAsync(
        int userId,
        string ticker,
        string? exchange,
        CancellationToken cancellationToken);

    Task<Company?> FindByLegalNameAsync(int userId, string legalName, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Company> Items, int Total)> SearchAsync(
        int userId,
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task AddAsync(Company company, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
