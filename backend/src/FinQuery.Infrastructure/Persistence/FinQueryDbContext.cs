using FinQuery.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FinQuery.Infrastructure.Persistence;

public sealed class FinQueryDbContext(DbContextOptions<FinQueryDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<FinQuery.Domain.Companies.Company> Companies => Set<FinQuery.Domain.Companies.Company>();

    public DbSet<FinQuery.Domain.Documents.Document> Documents => Set<FinQuery.Domain.Documents.Document>();

    public DbSet<FinQuery.Domain.Metrics.FinancialMetric> FinancialMetrics => Set<FinQuery.Domain.Metrics.FinancialMetric>();

    public DbSet<FinQuery.Domain.Documents.DocumentPage> DocumentPages => Set<FinQuery.Domain.Documents.DocumentPage>();

    public DbSet<FinQuery.Domain.Documents.DocumentSection> DocumentSections => Set<FinQuery.Domain.Documents.DocumentSection>();

    public DbSet<FinQuery.Domain.Documents.DocumentChunk> DocumentChunks => Set<FinQuery.Domain.Documents.DocumentChunk>();

    public DbSet<FinQuery.Domain.Tables.FinancialTable> FinancialTables => Set<FinQuery.Domain.Tables.FinancialTable>();

    public DbSet<FinQuery.Domain.Tables.FinancialTableRow> FinancialTableRows => Set<FinQuery.Domain.Tables.FinancialTableRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinQueryDbContext).Assembly);
    }
}
