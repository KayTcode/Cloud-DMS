using CleanArchCqrs.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", table =>
            table.HasCheckConstraint("CK_OutboxMessages_Version_Positive", "`Version` > 0"));

        builder.HasKey(message => message.Id);
        builder.Property(message => message.AggregateKey).IsRequired().HasMaxLength(255);
        builder.Property(message => message.Version).IsRequired();
        builder.Property(message => message.Payload).IsRequired().HasColumnType("json");
        builder.Property(message => message.PublishedAt);

        builder.HasIndex(message => new { message.AggregateKey, message.Version })
            .IsUnique();
        builder.HasIndex(message => new { message.PublishedAt, message.CreatedAt });
    }
}
