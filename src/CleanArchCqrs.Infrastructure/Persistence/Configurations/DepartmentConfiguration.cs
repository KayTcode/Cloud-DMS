using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments", table =>
        {
            table.HasCheckConstraint("CK_Departments_QuotaBytes_NonNegative", "`QuotaBytes` >= 0");
            table.HasCheckConstraint("CK_Departments_UsedBytes_NonNegative", "`UsedBytes` >= 0");
            table.HasCheckConstraint("CK_Departments_UsedBytes_Lte_QuotaBytes", "`UsedBytes` <= `QuotaBytes`");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(d => d.QuotaBytes)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(d => d.UsedBytes)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.HasIndex(d => new { d.TenantId, d.Code })
            .IsUnique();

        builder.HasOne(d => d.Tenant)
            .WithMany(t => t.Departments)
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.UserStorageQuotas)
            .WithOne(quota => quota.Department)
            .HasForeignKey(quota => quota.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
