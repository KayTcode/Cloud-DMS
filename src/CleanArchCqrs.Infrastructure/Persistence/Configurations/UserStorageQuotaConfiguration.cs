using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public class UserStorageQuotaConfiguration : IEntityTypeConfiguration<UserStorageQuota>
{
    public void Configure(EntityTypeBuilder<UserStorageQuota> builder)
    {
        builder.ToTable("UserStorageQuotas", table =>
        {
            table.HasCheckConstraint("CK_UserStorageQuotas_QuotaBytes_NonNegative", "`QuotaBytes` >= 0");
            table.HasCheckConstraint("CK_UserStorageQuotas_UsedBytes_NonNegative", "`UsedBytes` >= 0");
            table.HasCheckConstraint("CK_UserStorageQuotas_UsedBytes_Lte_QuotaBytes", "`UsedBytes` <= `QuotaBytes`");
        });

        builder.HasKey(q => q.Id);

        builder.Property(q => q.QuotaBytes)
            .IsRequired();

        builder.Property(q => q.UsedBytes)
            .IsRequired();

        builder.HasOne(q => q.User)
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(q => new { q.UserId, q.DepartmentId })
            .IsUnique();
    }
}
