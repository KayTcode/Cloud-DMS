using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationScopeConfiguration : IEntityTypeConfiguration<AuthorizationScope>
{
    public void Configure(EntityTypeBuilder<AuthorizationScope> builder)
    {
        builder.ToTable("AuthorizationScopes", table =>
            table.HasCheckConstraint("CK_AuthorizationScopes_Version_Positive", "`Version` > 0"));

        builder.HasKey(scope => scope.Id);
        builder.Property(scope => scope.ScopeKey).IsRequired().HasMaxLength(255);
        builder.Property(scope => scope.Version).IsRequired().HasDefaultValue(1L);
        builder.Property(scope => scope.IsUpdating).IsRequired();
        builder.Property(scope => scope.ActiveOperationId);

        builder.HasIndex(scope => scope.ScopeKey).IsUnique();
        builder.HasIndex(scope => scope.IsUpdating);
    }
}
