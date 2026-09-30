using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;

namespace AiIntelligence.Application.Tests;

public sealed class AiRelevanceScorerTests
{
    [Fact]
    public void Score_PrioritizesAiFocusedSourcesOverRuntimeRelease()
    {
        var aiSource = new SourceDefinition(Guid.NewGuid(), "Microsoft AI DevBlog", "Vendor", SourceType.WebPage, SourceClass.CurrentOfficial, new Uri("https://example.com/ai"), true);
        var runtimeSource = new SourceDefinition(Guid.NewGuid(), "dotnet-runtime", "Vendor", SourceType.GitHubRelease, SourceClass.CurrentOfficial, new Uri("https://example.com/runtime"), true);
        var aiItem = CreateRawItem(aiSource.Id, "AI agent evaluation updates", "New agent evaluation workflow for MCP.");
        var runtimeItem = CreateRawItem(runtimeSource.Id, ".NET 9.0.19", "Release notes and dependency updates.");

        var aiScore = AiRelevanceScorer.Score(aiItem, aiSource);
        var runtimeScore = AiRelevanceScorer.Score(runtimeItem, runtimeSource);

        Assert.True(aiScore.Score > runtimeScore.Score);
    }

    [Fact]
    public void Score_RuntimeItemWithAiContentCanOutrankGenericRuntime()
    {
        var runtimeSource = new SourceDefinition(Guid.NewGuid(), "dotnet-runtime", "Vendor", SourceType.GitHubRelease, SourceClass.CurrentOfficial, new Uri("https://example.com/runtime"), true);
        var genericRuntime = CreateRawItem(runtimeSource.Id, ".NET 8.0.28", "Release notes with dependency updates.");
        var aiRuntime = CreateRawItem(runtimeSource.Id, ".NET 9 AI enhancements", "Adds AI model inference runtime optimizations and model context protocol support.");

        var genericScore = AiRelevanceScorer.Score(genericRuntime, runtimeSource);
        var aiScore = AiRelevanceScorer.Score(aiRuntime, runtimeSource);

        Assert.True(aiScore.Score > genericScore.Score);
    }

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string title, string content)
    {
        return new RawSourceItem(
            Guid.NewGuid(),
            sourceDefinitionId,
            title,
            new Uri("https://example.com/item"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            content,
            new ContentHash(Guid.NewGuid().ToString("N")));
    }
}
