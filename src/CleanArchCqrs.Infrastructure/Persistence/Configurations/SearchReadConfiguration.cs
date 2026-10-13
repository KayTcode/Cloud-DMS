using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class SearchReadConfiguration :
    IEntityTypeConfiguration<FileContentVersion>, IEntityTypeConfiguration<FileSearchGeneration>,
    IEntityTypeConfiguration<FileTextChunk>, IEntityTypeConfiguration<FilePreviewArtifact>,
    IEntityTypeConfiguration<DocumentOutboxMessage>, IEntityTypeConfiguration<FileUploadSession>,
    IEntityTypeConfiguration<Team>, IEntityTypeConfiguration<TeamMember>, IEntityTypeConfiguration<ResourceGrant>
{
    private static void Tenant<T>(EntityTypeBuilder<T> b) where T : BaseEntity
    {
        b.HasKey(x => x.Id);
        b.HasOne<CleanArchCqrs.Domain.Entities.Tenant>().WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        b.HasIndex("TenantId");
    }
    private static void File<T>(EntityTypeBuilder<T> b) where T : BaseEntity =>
        b.HasOne<FileEntry>().WithMany().HasForeignKey("TenantId", "FileId")
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    private static void Version<T>(EntityTypeBuilder<T> b) where T : BaseEntity =>
        b.HasOne<FileContentVersion>().WithMany().HasForeignKey("TenantId", "FileId", "DocumentVersion")
            .HasPrincipalKey(x => new { x.TenantId, x.FileId, x.DocumentVersion }).OnDelete(DeleteBehavior.Restrict);
    public void Configure(EntityTypeBuilder<FileContentVersion> b)
    {
        Tenant(b); File(b);
        b.HasAlternateKey(x => new { x.TenantId, x.FileId, x.DocumentVersion });
        b.HasIndex(x => new { x.FileId, x.DocumentVersion }).IsUnique();
        b.Property(x => x.Hash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(255).IsRequired();
        b.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(32);
        b.HasOne<StorageProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<FileSearchGeneration> b)
    {
        Tenant(b); Version(b);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasAlternateKey(x => new { x.TenantId, x.FileId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.FileId, x.DocumentVersion, x.Status });
        b.Property(x => x.ModelVersion).HasMaxLength(128).IsRequired();
        b.Property(x => x.ExtractorVersion).HasMaxLength(128).IsRequired();
        b.Property(x => x.ChunkerVersion).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.FailureCode).HasMaxLength(128);
    }
    public void Configure(EntityTypeBuilder<FileTextChunk> b)
    {
        Tenant(b);
        b.HasOne<FileSearchGeneration>().WithMany().HasForeignKey(x => new { x.TenantId, x.GenerationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.GenerationId, x.Ordinal }).IsUnique();
        b.Property(x => x.Text).HasColumnType("longtext").IsRequired();
        b.HasIndex(x => x.Text).IsFullText();
        b.Property(x => x.Location).HasMaxLength(500);
    }
    public void Configure(EntityTypeBuilder<FilePreviewArtifact> b)
    {
        Tenant(b); Version(b);
        b.HasIndex(x => new { x.FileId, x.DocumentVersion, x.ConverterVersion, x.Format }).IsUnique();
        b.Property(x => x.ConverterVersion).HasMaxLength(128).IsRequired();
        b.Property(x => x.Format).HasMaxLength(32).IsRequired();
        b.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.HasOne<StorageProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DocumentOutboxMessage> b)
    {
        Tenant(b); File(b);
        b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt });
        b.HasIndex(x => new { x.TenantId, x.FileId, x.DocumentVersion });
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        b.Property(x => x.Payload).HasColumnType("longtext").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.FailureCode).HasMaxLength(128);
    }
    public void Configure(EntityTypeBuilder<FileUploadSession> b)
    {
        Tenant(b);
        // FileId is allocated before FileEntry exists; the upload saga persists it on completion.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.MultipartUploadId).HasMaxLength(500).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(255).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.CompletionManifestHash).HasMaxLength(64);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorageProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<Team> b)
    {
        Tenant(b);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.Name).HasMaxLength(255).IsRequired();
        b.Property(x => x.AuthorizationVersion).HasDefaultValue(1L).IsConcurrencyToken();
    }
    public void Configure(EntityTypeBuilder<TeamMember> b)
    {
        Tenant(b);
        b.HasOne<Team>().WithMany().HasForeignKey(x => new { x.TenantId, x.TeamId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.UserId });
    }
    public void Configure(EntityTypeBuilder<ResourceGrant> b)
    {
        Tenant(b);
        b.Property(x => x.ResourceKind).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.PrincipalKind).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        b.HasIndex(x => new { x.TenantId, x.ResourceKind, x.ResourceId, x.PrincipalKind, x.PrincipalId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.PrincipalKind, x.PrincipalId, x.ExpiresAt });
    }
}
