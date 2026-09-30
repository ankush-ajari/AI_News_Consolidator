using System.Text.Json;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

public sealed class RawSourceItemConfiguration : IEntityTypeConfiguration<RawSourceItem>
{
    public void Configure(EntityTypeBuilder<RawSourceItem> builder)
    {
        builder.ToTable("RawSourceItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SourceDefinitionId).IsRequired();
        builder.Property(item => item.SourceClass).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(500).IsRequired();
        builder.Property(item => item.Url).HasConversion(uri => uri.AbsoluteUri, value => new Uri(value)).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.CanonicalUrl).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.RawContent).IsRequired();
        builder.Property(item => item.EnrichedContent).IsRequired();
        builder.Property(item => item.ContentHash).HasConversion(hash => hash.Value, value => new ContentHash(value)).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ContentSourceUrls)
            .HasConversion(
                value => JsonSerializer.Serialize(value.Select(uri => uri.AbsoluteUri).ToArray(), (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<IReadOnlyCollection<string>>(value, (JsonSerializerOptions?)null)!
                    .Select(uri => new Uri(uri))
                    .ToArray())
            .HasColumnType("TEXT")
            .Metadata.SetValueComparer(UriCollectionComparer.Instance);
        builder.HasIndex(item => new { item.CanonicalUrl, item.ContentHash }).IsUnique();
        builder.HasOne<SourceDefinition>()
            .WithMany()
            .HasForeignKey(item => item.SourceDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
