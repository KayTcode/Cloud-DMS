using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public class FileShareConfiguration : IEntityTypeConfiguration<CleanArchCqrs.Domain.Entities.FileShare>
{
    public void Configure(EntityTypeBuilder<CleanArchCqrs.Domain.Entities.FileShare> builder)
    {
        builder.ToTable("FileShares");

        builder.HasKey(fs => fs.Id);

        builder.Property(fs => fs.CanRead)
            .IsRequired();

        builder.Property(fs => fs.CanWrite)
            .IsRequired();

        builder.Property(fs => fs.CanDelete)
            .IsRequired();

        builder.Property(fs => fs.ExpiresAt);

        builder.HasOne(fs => fs.FileEntry)
            .WithMany(f => f.Shares)
            .HasForeignKey(fs => fs.FileEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(fs => fs.SharedWithUser)
            .WithMany()
            .HasForeignKey(fs => fs.SharedWithUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(fs => new { fs.FileEntryId, fs.SharedWithUserId })
            .IsUnique();

        builder.HasIndex(fs => fs.SharedWithUserId);
        builder.HasIndex(fs => fs.ExpiresAt);
    }
}
