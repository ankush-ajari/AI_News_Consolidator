using AiIntelligence.Domain.Enums;
using AiIntelligence.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class SourceDefinitionConfigurationLoaderTests
{
    [Theory]
    [InlineData("CurrentOfficial", SourceClass.CurrentOfficial)]
    [InlineData("TrendResearch", SourceClass.TrendResearch)]
    [InlineData("ResearchDiscovery", SourceClass.ResearchDiscovery)]
    public void Load_BindsAllSourceClassValues(string sourceClassText, SourceClass expected)
    {
        var configuration = BuildConfiguration(new[]
        {
            Source("One", sourceClassText)
        });

        var source = Assert.Single(SourceDefinitionConfigurationLoader.Load(configuration));

        Assert.Equal(expected, source.SourceClass);
    }

    [Fact]
    public void Load_LoadsMixedConfigurationWithoutDroppingTrendResearch()
    {
        var configuration = BuildConfiguration(new[]
        {
            Source("Official 1", "CurrentOfficial"),
            Source("Official 2", "CurrentOfficial"),
            Source("Trend 1", "TrendResearch"),
            Source("Trend 2", "TrendResearch"),
            Source("Trend 3", "TrendResearch"),
            Source("Discovery 1", "ResearchDiscovery")
        });

        var sources = SourceDefinitionConfigurationLoader.Load(configuration);

        Assert.Equal(6, sources.Count);
        Assert.Equal(2, sources.Count(source => source.SourceClass == SourceClass.CurrentOfficial));
        Assert.Equal(3, sources.Count(source => source.SourceClass == SourceClass.TrendResearch));
        Assert.Equal(1, sources.Count(source => source.SourceClass == SourceClass.ResearchDiscovery));
    }

    private static IConfiguration BuildConfiguration(IEnumerable<Dictionary<string, string?>> sources)
    {
        var values = new Dictionary<string, string?>();
        var index = 0;
        foreach (var source in sources)
        {
            foreach (var pair in source)
            {
                values[$"Sources:Definitions:{index}:{pair.Key}"] = pair.Value;
            }

            index++;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Load_RequiresStableIdForTrendResearch()
    {
        var trendWithoutId = Source("Trend Missing Id", "TrendResearch");
        trendWithoutId.Remove("id");
        var configuration = BuildConfiguration(new[] { trendWithoutId });

        var exception = Assert.Throws<InvalidOperationException>(() => SourceDefinitionConfigurationLoader.Load(configuration));

        Assert.Contains("must define a stable", exception.Message);
    }

    private static Dictionary<string, string?> Source(string name, string sourceClass) => new()
    {
        ["id"] = Guid.NewGuid().ToString(),
        ["name"] = name,
        ["vendor"] = "Vendor",
        ["sourceType"] = "WebPage",
        ["sourceClass"] = sourceClass,
        ["url"] = $"https://example.com/{Uri.EscapeDataString(name)}",
        ["isEnabled"] = "true"
    };
}
