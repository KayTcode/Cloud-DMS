using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class LegacyUploadIntentConfiguration : IEntityTypeConfiguration<LegacyUploadIntent>
{
    public void Configure(EntityTypeBuilder<LegacyUploadIntent> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.HasIndex(x => new { x.TenantId, x.FileId }).IsUnique();
        b.Property(x => x.ParentFolderId).HasMaxLength(500);
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorageProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
    }
}
