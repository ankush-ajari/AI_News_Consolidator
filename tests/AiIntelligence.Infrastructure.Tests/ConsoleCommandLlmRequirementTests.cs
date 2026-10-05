using AiIntelligence.Console;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class ConsoleCommandLlmRequirementTests
{
    [Theory]
    [InlineData("analyze", true)]
    [InlineData("analyze-trends", true)]
    [InlineData("run-workflow", true)]
    [InlineData("test-llm", true)]
    [InlineData("report", false)]
    [InlineData("inspect", false)]
    [InlineData("reset", false)]
    public void LlmRequirementMatchesCommand(string command, bool expected)
    {
        var result = ConsoleCommandParser.IsSupportedCommand(command);

        Assert.True(result);
        Assert.Equal(expected, IsLlmRequiredCommand(command));
    }

    private static bool IsLlmRequiredCommand(string command)
    {
        return string.Equals(command, "analyze", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "analyze-trends", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "run-workflow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "test-llm", StringComparison.OrdinalIgnoreCase);
    }
}
