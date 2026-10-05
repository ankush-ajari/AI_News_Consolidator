using AiIntelligence.Application.Sources;

namespace AiIntelligence.Application.Tests;

public sealed class RawContentComparerTests
{
    [Fact]
    public void NormalizeCanonicalUrl_RemovesTrackingAndTrailingSlash()
    {
        var normalized = RawContentComparer.NormalizeCanonicalUrl("https://Example.com/report/?utm_source=test#section");

        Assert.Equal("https://example.com/report", normalized);
    }

    [Fact]
    public void IsMateriallyDifferent_IgnoresMinorWhitespaceChanges()
    {
        var left = "Headline\n\nSummary with extra   spaces";
        var right = "Headline Summary with extra spaces";

        Assert.False(RawContentComparer.IsMateriallyDifferent(left, right));
    }

    [Fact]
    public void IsMateriallyDifferent_FlagsLargeContentChange()
    {
        var left = new string('a', 5000);
        var right = new string('b', 5000);

        Assert.True(RawContentComparer.IsMateriallyDifferent(left, right));
    }
}
