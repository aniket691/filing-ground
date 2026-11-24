using Microsoft.EntityFrameworkCore;
using Entities;

namespace Backend;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Company> Companies { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<FinancialMetric> FinancialMetrics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User entity configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.HashedPassword).IsRequired().HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Company entity configuration
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.LegalName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).HasMaxLength(255);
            entity.Property(e => e.Ticker).HasMaxLength(32);
            entity.Property(e => e.Exchange).HasMaxLength(64);
            entity.Property(e => e.Country).HasMaxLength(2);
            entity.Property(e => e.Industry).HasMaxLength(128);
            entity.Property(e => e.Sector).HasMaxLength(128);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Ticker, e.Exchange })
                .IsUnique(true);

            entity.HasIndex(e => new { e.UserId, e.LegalName.ToLower() })
                .IsUnique(true);
        });

        // Document entity configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Title).IsRequired().HasMaxLength(512);
            entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.FilingDate).HasColumnType("date");
            entity.Property(e => e.FiscalYear).HasColumnType("int");
            entity.Property(e => e.FiscalQuarter).HasMaxLength(2);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.Language).HasMaxLength(8).HasDefaultValue("en");
            entity.Property(e => e.FilePath).HasMaxLength(1024);
            entity.Property(e => e.FileHash).HasMaxLength(64);
            entity.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(32);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Company)
                .WithMany()
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.ProcessingError).HasColumnType("nvarchar(max)");

            entity.HasIndex(e => new { e.UserId, e.CompanyId, e.FileHash })
                .IsUnique(true);
        });

        // FinancialMetric entity configuration
        modelBuilder.Entity<FinancialMetric>(entity =>
        {
            entity.ToTable("financial_metrics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.MetricName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.NormalizedMetricName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Value).HasColumnType("decimal(30, 10)");
            entity.Property(e => e.Unit).HasMaxLength(32);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.PeriodStart).HasColumnType("date");
            entity.Property(e => e.PeriodEnd).HasColumnType("date");
            entity.Property(e => e.FiscalYear).HasColumnType("int");
            entity.Property(e => e.FiscalQuarter).HasMaxLength(2);
            entity.Property(e => e.SourcePage).HasColumnType("int");
            entity.Property(e => e.Confidence).HasColumnType("decimal(4, 3)");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Company)
                .WithMany()
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.CompanyId, e.NormalizedMetricName, e.PeriodStart, e.PeriodEnd });
            entity.HasIndex(e => new { e.CompanyId, e.FiscalYear, e.FiscalQuarter });
        });
    }
}