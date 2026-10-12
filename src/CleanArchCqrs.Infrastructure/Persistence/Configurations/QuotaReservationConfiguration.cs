using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class QuotaReservationConfiguration : IEntityTypeConfiguration<QuotaReservation>
{
    public void Configure(EntityTypeBuilder<QuotaReservation> builder)
    {
        builder.ToTable("QuotaReservations", table =>
            table.HasCheckConstraint("CK_QuotaReservations_SizeBytes_Positive", "`SizeBytes` > 0"));

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(reservation => reservation.SizeBytes).IsRequired();
        builder.Property(reservation => reservation.StorageKey).HasMaxLength(500);
        builder.Property(reservation => reservation.State)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(reservation => reservation.IdempotencyKey).IsUnique();
        builder.HasIndex(reservation => new { reservation.UserId, reservation.State });
        builder.HasIndex(reservation => new { reservation.DepartmentId, reservation.State });
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.State });

        builder.HasOne(reservation => reservation.User)
            .WithMany()
            .HasForeignKey(reservation => reservation.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(reservation => reservation.Department)
            .WithMany()
            .HasForeignKey(reservation => reservation.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(reservation => reservation.Tenant)
            .WithMany()
            .HasForeignKey(reservation => reservation.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
