using FinQuery.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinQuery.Infrastructure.Persistence;

/// <summary>
/// Maps the existing <c>users</c> table. Indexes are not declared here.
/// The case-insensitive unique index <c>uq_users_email_lower</c> stays in
/// MySQL, owned by Alembic. EF must not generate or apply migrations.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(user => user.HashedPassword).HasColumnName("hashed_password").HasMaxLength(255).IsRequired();
        builder.Property(user => user.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
