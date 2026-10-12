using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationOperationConfiguration : IEntityTypeConfiguration<AuthorizationOperation>
{
    public void Configure(EntityTypeBuilder<AuthorizationOperation> builder)
    {
        builder.ToTable("AuthorizationOperations", table =>
            table.HasCheckConstraint("CK_AuthorizationOperations_Version_Positive", "`Version` > 0"));

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.InitiatedByUserId);
        builder.Property(operation => operation.ScopeKey).IsRequired().HasMaxLength(255);
        builder.Property(operation => operation.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.Property(operation => operation.PayloadHash).IsRequired().HasMaxLength(128);
        builder.Property(operation => operation.Version).IsRequired();
        builder.Property(operation => operation.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(operation => operation.CommittedAt);

        builder.HasIndex(operation => new { operation.ScopeKey, operation.IdempotencyKey })
            .IsUnique();
        builder.HasIndex(operation => new { operation.ScopeKey, operation.Version })
            .IsUnique();
        builder.HasIndex(operation => new { operation.Status, operation.CreatedAt });
        builder.HasIndex(operation => new { operation.InitiatedByUserId, operation.CreatedAt });
    }
}
