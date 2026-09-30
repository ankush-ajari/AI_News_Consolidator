using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Intelligence;

public sealed record AiRelevanceScore(
    int Score,
    IReadOnlyCollection<string> PositiveSignals,
    IReadOnlyCollection<string> NegativeSignals);

public static class AiRelevanceScorer
{
    private static readonly string[] AiFocusedSources =
    {
        "Microsoft AI DevBlog",
        "Microsoft Foundry DevBlog",
        "Engineering at Microsoft AI"
    };

    private static readonly string[] PositiveSignals =
    {
        "ai",
        "artificial intelligence",
        "agent",
        "agentic",
        "model",
        "llm",
        "foundry",
        "copilot",
        "evaluation",
        "rag",
        "retrieval",
        "inference",
        "reasoning",
        "multimodal",
        "sdk",
        "ai framework",
        "model context protocol",
        "mcp"
    };

    private static readonly string[] NegativeSignals =
    {
        "runtime release",
        "servicing release",
        "release notes",
        "dependency update",
        "dependencies",
        "test fix",
        "tests only",
        "package update",
        "package version",
        "release management",
        "security update"
    };

    public static AiRelevanceScore Score(RawSourceItem rawItem, SourceDefinition? sourceDefinition)
    {
        var content = BuildContent(rawItem, sourceDefinition);
        var positiveMatches = MatchSignals(content, PositiveSignals);
        var negativeMatches = MatchSignals(content, NegativeSignals);
        var score = 0;

        if (IsAiFocusedSource(sourceDefinition))
        {
            score += 2;
            positiveMatches = positiveMatches.Append($"source:{sourceDefinition!.Name}").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        score += positiveMatches.Count * 4;
        score -= negativeMatches.Count * 2;

        return new AiRelevanceScore(score, positiveMatches, negativeMatches);
    }

    private static string BuildContent(RawSourceItem rawItem, SourceDefinition? sourceDefinition)
    {
        return string.Concat(
            sourceDefinition?.Name ?? string.Empty,
            " ",
            rawItem.Title,
            " ",
            rawItem.RawContent ?? string.Empty,
            " ",
            rawItem.EnrichedContent ?? string.Empty);
    }

    private static IReadOnlyCollection<string> MatchSignals(string content, IReadOnlyCollection<string> signals)
    {
        return signals
            .Where(signal => content.Contains(signal, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsAiFocusedSource(SourceDefinition? sourceDefinition)
    {
        return sourceDefinition is not null
            && AiFocusedSources.Any(name => sourceDefinition.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
