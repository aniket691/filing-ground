using FinQuery.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinQuery.Infrastructure.Persistence;

/// <summary>
/// Maps the existing <c>companies</c> table. Functional and partial unique
/// indexes stay in MySQL under Alembic. They are not declared here, so EF
/// will not try to create them.
/// </summary>
public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");
        builder.HasKey(company => company.Id);
        builder.Property(company => company.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(company => company.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(company => company.LegalName).HasColumnName("legal_name").HasMaxLength(255).IsRequired();
        builder.Property(company => company.DisplayName).HasColumnName("display_name").HasMaxLength(255);
        builder.Property(company => company.Ticker).HasColumnName("ticker").HasMaxLength(32);
        builder.Property(company => company.Exchange).HasColumnName("exchange").HasMaxLength(64);
        builder.Property(company => company.Country).HasColumnName("country").HasMaxLength(2);
        builder.Property(company => company.Industry).HasColumnName("industry").HasMaxLength(128);
        builder.Property(company => company.Sector).HasColumnName("sector").HasMaxLength(128);
        builder.Property(company => company.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(company => company.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasOne<FinQuery.Domain.Users.User>()
            .WithMany()
            .HasForeignKey(company => company.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
