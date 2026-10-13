using CleanArchCqrs.Domain.Entities;
using CleanArchCqrs.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public class FileEntryConfiguration : IEntityTypeConfiguration<FileEntry>
{
    public void Configure(EntityTypeBuilder<FileEntry> builder)
    {
        builder.ToTable("FileEntries");

        builder.HasKey(f => f.Id);
        builder.HasAlternateKey(f => new { f.TenantId, f.Id });
        builder.Property(f => f.DocumentVersion).HasDefaultValue(1L).IsConcurrencyToken();
        builder.HasOne<FileSearchGeneration>().WithMany()
            .HasForeignKey(f => new { f.TenantId, f.Id, f.ActiveSearchGenerationId })
            .HasPrincipalKey(g => new { g.TenantId, g.FileId, g.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(f => f.StorageKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.ContentType)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(f => f.SizeBytes)
            .IsRequired();

        builder.Property(f => f.Hash)
            .HasMaxLength(128);

        builder.Property(f => f.IsDeleted)
            .IsRequired();

        builder.Property(f => f.OwningDepartmentId);

        builder.Property(f => f.Lifecycle)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(FileLifecycle.Active)
            .IsRequired();

        builder.Property(f => f.AuthorizationVersion)
            .IsRequired()
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.Property(f => f.QuotaReleasedAt);

        builder.HasOne(f => f.Tenant)
            .WithMany(t => t.Files)
            .HasForeignKey(f => f.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Owner)
            .WithMany(u => u.Files)
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.OwningDepartment)
            .WithMany()
            .HasForeignKey(f => f.OwningDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Folder)
            .WithMany(folder => folder.Files)
            .HasForeignKey(f => f.FolderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Department)
            .WithMany(d => d.Files)
            .HasForeignKey(f => f.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.DeletedByUser)
            .WithMany()
            .HasForeignKey(f => f.DeletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(f => f.TenantId);
        builder.HasIndex(f => new { f.TenantId, f.Lifecycle, f.IsDeleted, f.FolderId });
        builder.HasIndex(f => f.Name).IsFullText();
        builder.HasIndex(f => f.OwnerId);
        builder.HasIndex(f => f.FolderId);
        builder.HasIndex(f => f.OwningDepartmentId);
        builder.HasIndex(f => f.StorageKey)
            .IsUnique();
    }
}
