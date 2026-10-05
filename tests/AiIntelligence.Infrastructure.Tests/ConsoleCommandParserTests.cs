using AiIntelligence.Console;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class ConsoleCommandParserTests
{
    [Fact]
    public void IsSupportedCommand_IncludesRunWorkflow()
    {
        Assert.True(ConsoleCommandParser.IsSupportedCommand("run-workflow"));
    }

    [Fact]
    public void IsSupportedCommand_PreservesExistingCommands()
    {
        var commands = new[] { "fetch", "ingest", "analyze", "analyze-trends", "inspect", "reset", "report", "test-llm", "backfill-concepts" };

        foreach (var command in commands)
        {
            Assert.True(ConsoleCommandParser.IsSupportedCommand(command));
        }
    }

    [Theory]
    [InlineData("--current-limit", "3", 3)]
    [InlineData("--trend-limit", "5", 5)]
    public void GetIntOption_ParsesWorkflowLimits(string option, string value, int expected)
    {
        var args = new[] { "run-workflow", option, value };

        var result = ConsoleCommandParser.GetIntOption(args, option);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("--skip-ingest")]
    [InlineData("--mock-llm")]
    public void HasFlag_RecognizesWorkflowFlags(string flag)
    {
        var args = new[] { "run-workflow", flag };

        Assert.True(ConsoleCommandParser.HasFlag(args, flag));
    }
}
