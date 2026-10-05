using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;

namespace AiIntelligence.Domain.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void SourceDefinition_Creates_WithExpectedValues()
    {
        var id = Guid.NewGuid();
        var url = new Uri("https://example.com/ai/rss");

        var source = new SourceDefinition(
            id,
            "Microsoft AI Blog",
            "Microsoft",
            SourceType.Rss,
            SourceClass.CurrentOfficial,
            url,
            isEnabled: true);

        Assert.Equal(id, source.Id);
        Assert.Equal("Microsoft AI Blog", source.Name);
        Assert.Equal("Microsoft", source.Vendor);
        Assert.Equal(SourceType.Rss, source.SourceType);
        Assert.Equal(SourceClass.CurrentOfficial, source.SourceClass);
        Assert.Equal(url, source.Url);
        Assert.True(source.IsEnabled);
    }

    [Fact]
    public void SourceDefinition_Requires_Name()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SourceDefinition(
            Guid.NewGuid(),
            " ",
            "Microsoft",
            SourceType.Rss,
            SourceClass.CurrentOfficial,
            new Uri("https://example.com"),
            isEnabled: true));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void RawSourceItem_Requires_ContentHash()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new RawSourceItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Announcement",
            new Uri("https://example.com/announcement"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Raw text",
            contentHash: null!));

        Assert.Equal("contentHash", exception.ParamName);
    }

    [Fact]
    public void IntelligenceItem_Creates_WithPersonaIndependentValues()
    {
        var sourceUrl = new Uri("https://example.com/release");

        var item = new IntelligenceItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "OpenAI",
            "Agent tooling",
            "Developer Tools",
            "Example SDK",
            "New capabilities were announced.",
            new[] { "Automation", "Tool use" },
            new[] { "Preview limitations" },
            "Preview",
            SourceClass.CurrentOfficial,
            DateTimeOffset.UtcNow,
            sourceUrl,
            new[] { AIConceptTag.AgenticAI });

        Assert.Equal("OpenAI", item.Vendor);
        Assert.Equal("Agent tooling", item.Topic);
        Assert.Equal(SourceClass.CurrentOfficial, item.SourceClass);
        Assert.Contains("Automation", item.Capabilities);
        Assert.Equal(sourceUrl, item.SourceUrl);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void TrendEvidence_Rejects_InvalidConfidence(decimal confidence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new TrendEvidence(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AI adoption",
            "2026 Q3",
            TrendEvidencePeriodProvenance.SourceContent,
            "Adoption increased.",
            "Survey evidence.",
            confidence,
            new Uri("https://example.com/research"),
            conceptTags: new[] { AIConceptTag.AIInvestment }));

        Assert.Equal("confidence", exception.ParamName);
    }

    [Fact]
    public void PersonaInsight_Creates_ForEachPersonaType()
    {
        foreach (var personaType in Enum.GetValues<PersonaType>())
        {
            var insight = new PersonaInsight(
                personaType,
                PersonaRelevance.Medium,
                "Headline",
                "It affects planning and execution.",
                "Monitor");

            Assert.Equal(personaType, insight.PersonaType);
            Assert.Equal(PersonaRelevance.Medium, insight.Relevance);
            Assert.Equal("Headline", insight.Headline);
        }
    }

    [Fact]
    public void ContentHash_Uses_ValueEquality()
    {
        var first = new ContentHash("abc123");
        var second = new ContentHash("abc123");
        var third = new ContentHash("def456");

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void ContentHash_Trims_Value()
    {
        var hash = new ContentHash("  abc123  ");

        Assert.Equal("abc123", hash.Value);
        Assert.Equal("abc123", hash.ToString());
    }
}
