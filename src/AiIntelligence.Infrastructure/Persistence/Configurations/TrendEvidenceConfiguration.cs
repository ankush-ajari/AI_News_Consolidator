using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiIntelligence.Infrastructure.Persistence.Configurations;

public sealed class TrendEvidenceConfiguration : IEntityTypeConfiguration<TrendEvidence>
{
    public void Configure(EntityTypeBuilder<TrendEvidence> builder)
    {
        builder.ToTable("TrendEvidence");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.SourceItemId).IsRequired();
        builder.Property(item => item.Topic).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Period).HasMaxLength(100).IsRequired();
        builder.Property(item => item.PeriodProvenance).HasMaxLength(50).IsRequired();
        builder.Property(item => item.Finding).IsRequired();
        builder.Property(item => item.QuantitativeEvidence).IsRequired();
        builder.Property(item => item.EvidenceSummary).IsRequired();
        builder.Property(item => item.Confidence).HasPrecision(5, 4).IsRequired();
        builder.Property(item => item.SourceUrl).HasConversion(uri => uri.AbsoluteUri, value => new Uri(value)).HasMaxLength(2048).IsRequired();
        builder.Property(item => item.PublicationName).HasMaxLength(300).IsRequired();
        builder.HasOne<RawSourceItem>()
            .WithMany()
            .HasForeignKey(item => item.SourceItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
