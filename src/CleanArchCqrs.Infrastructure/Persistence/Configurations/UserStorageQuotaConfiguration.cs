using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public class UserStorageQuotaConfiguration : IEntityTypeConfiguration<UserStorageQuota>
{
    public void Configure(EntityTypeBuilder<UserStorageQuota> builder)
    {
        builder.ToTable("UserStorageQuotas");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.QuotaBytes)
            .IsRequired();

        builder.Property(q => q.UsedBytes)
            .IsRequired();

        builder.HasOne(q => q.User)
            .WithOne()
            .HasForeignKey<UserStorageQuota>(q => q.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(q => q.UserId)
            .IsUnique();
    }
}
