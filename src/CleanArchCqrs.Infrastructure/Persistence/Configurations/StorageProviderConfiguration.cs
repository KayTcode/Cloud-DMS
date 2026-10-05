using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public class StorageProviderConfiguration : IEntityTypeConfiguration<StorageProvider>
{
    public void Configure(EntityTypeBuilder<StorageProvider> builder)
    {
        builder.ToTable("StorageProviders");

        builder.HasKey(sp => sp.Id);

        builder.Property(sp => sp.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(sp => sp.Type)
            .IsRequired();

        builder.Property(sp => sp.ConfigEncrypted)
            .IsRequired();

        builder.Property(sp => sp.Status)
            .HasMaxLength(50)
            .HasDefaultValue("Connected");

        builder.Property(sp => sp.RootFolderId)
            .HasMaxLength(255);

        builder.HasMany(sp => sp.Tenants)
            .WithOne(t => t.StorageProvider)
            .HasForeignKey(t => t.StorageProviderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(sp => sp.Files)
            .WithOne(f => f.StorageProvider)
            .HasForeignKey(f => f.StorageProviderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
