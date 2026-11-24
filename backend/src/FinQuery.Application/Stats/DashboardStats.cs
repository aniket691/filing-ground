namespace FinQuery.Application.Stats;

public sealed record DashboardStats(int Documents, int Companies, int FinancialMetrics);

public interface IDashboardStatsReader
{
    Task<DashboardStats> ReadAsync(int userId, CancellationToken cancellationToken);
}
