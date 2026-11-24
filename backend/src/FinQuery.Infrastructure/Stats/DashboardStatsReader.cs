using FinQuery.Application.Stats;
using FinQuery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Stats;

public sealed class DashboardStatsReader(FinQueryDbContext db) : IDashboardStatsReader
{
    public async Task<DashboardStats> ReadAsync(int userId, CancellationToken cancellationToken)
    {
        var documents = await db.Documents.CountAsync(document => document.UserId == userId, cancellationToken);
        var companies = await db.Companies.CountAsync(company => company.UserId == userId, cancellationToken);
        var financialMetrics = await (
            from metric in db.FinancialMetrics
            join company in db.Companies on metric.CompanyId equals company.Id
            where company.UserId == userId
            select metric).CountAsync(cancellationToken);

        return new DashboardStats(documents, companies, financialMetrics);
    }
}
