using AiIntelligence.Console;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class ConsoleLlmModeDeciderTests
{
    [Theory]
    [InlineData("report", false, false, true)]
    [InlineData("report", false, true, false)]
    [InlineData("report", true, false, true)]
    [InlineData("analyze", true, false, true)]
    [InlineData("analyze", false, false, false)]
    public void ShouldUseMock_RespectsCommandAndFlags(string command, bool mockFlag, bool hasApiKey, bool expected)
    {
        var result = ConsoleLlmModeDecider.ShouldUseMock(command, mockFlag, hasApiKey);

        Assert.Equal(expected, result);
    }
}
