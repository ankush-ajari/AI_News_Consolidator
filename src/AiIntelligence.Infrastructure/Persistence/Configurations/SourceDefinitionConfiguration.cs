using System.Text.Json;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

public sealed class SourceDefinitionConfiguration : IEntityTypeConfiguration<SourceDefinition>
{
    public void Configure(EntityTypeBuilder<SourceDefinition> builder)
    {
        builder.ToTable("SourceDefinitions");
        builder.HasKey(source => source.Id);
        builder.Property(source => source.Name).HasMaxLength(200).IsRequired();
        builder.Property(source => source.Vendor).HasMaxLength(100).IsRequired();
        builder.Property(source => source.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(source => source.SourceClass).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(source => source.Url).HasConversion(uri => uri.AbsoluteUri, value => new Uri(value)).HasMaxLength(2048).IsRequired();
        builder.Property(source => source.IncludeTopicHints)
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<IReadOnlyCollection<string>>(value, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
            .HasColumnType("TEXT")
            .Metadata.SetValueComparer(StringCollectionComparer.Instance);
        builder.HasIndex(source => source.Name).IsUnique();
    }
}
