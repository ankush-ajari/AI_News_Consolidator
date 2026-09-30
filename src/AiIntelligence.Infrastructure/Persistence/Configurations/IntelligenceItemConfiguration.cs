using System.Text.Json;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

public sealed class IntelligenceItemConfiguration : IEntityTypeConfiguration<IntelligenceItem>
{
    public void Configure(EntityTypeBuilder<IntelligenceItem> builder)
    {
        builder.ToTable("IntelligenceItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SourceItemId).IsRequired();
        builder.Property(item => item.Vendor).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Topic).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Category).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ProductOrFramework).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Summary).IsRequired();
        builder.Property(item => item.ReleaseStage).HasMaxLength(100).IsRequired();
        builder.Property(item => item.SourceClass).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(item => item.SourceUrl).HasConversion(uri => uri.AbsoluteUri, value => new Uri(value)).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.Capabilities)
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<IReadOnlyCollection<string>>(value, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
            .HasColumnType("TEXT")
            .Metadata.SetValueComparer(StringCollectionComparer.Instance);
        builder.Property(item => item.Limitations)
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<IReadOnlyCollection<string>>(value, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
            .HasColumnType("TEXT")
            .Metadata.SetValueComparer(StringCollectionComparer.Instance);
    }
}
