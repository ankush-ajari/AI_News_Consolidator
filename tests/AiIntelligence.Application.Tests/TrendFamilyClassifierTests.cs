using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Tests;

public sealed class TrendFamilyClassifierTests
{
    private readonly ITrendFamilyClassifier _classifier = new DeterministicTrendFamilyClassifier();

    [Theory]
    [InlineData("AI agent task performance gains", "Agent task completion gains on OSWorld", TrendFamily.AgentTaskPerformance)]
    [InlineData("U.S.-China model performance gap", "US-China model performance convergence", TrendFamily.USChinaModelGap)]
    [InlineData("Open vs. closed model performance gap", "Closed models regain lead over open models", TrendFamily.OpenClosedModelGap)]
    [InlineData("Benchmark reliability and gaming concerns", "Invalid benchmark questions and leaderboard gaming", TrendFamily.BenchmarkReliability)]
    public void Classify_ReturnsExpectedFamily(string topic, string finding, TrendFamily expected)
    {
        var family = _classifier.Classify(topic, finding, string.Empty, Array.Empty<AIConceptTag>());

        Assert.Equal(expected, family);
    }

    [Fact]
    public void Classify_DistinguishesBenchmarkFamilies()
    {
        var saturation = _classifier.Classify("Benchmark saturation", "Benchmarks saturate quickly", string.Empty, Array.Empty<AIConceptTag>());
        var reliability = _classifier.Classify("Benchmark reliability", "Invalid benchmark questions", string.Empty, Array.Empty<AIConceptTag>());

        Assert.Equal(TrendFamily.BenchmarkSaturation, saturation);
        Assert.Equal(TrendFamily.BenchmarkReliability, reliability);
    }

    [Fact]
    public void Classify_DistinguishesModelGapFamilies()
    {
        var usChina = _classifier.Classify("U.S.-China model gap", "US-China model performance convergence", string.Empty, Array.Empty<AIConceptTag>());
        var openClosed = _classifier.Classify("Open-vs-closed model gap", "Closed models regain lead over open models", string.Empty, Array.Empty<AIConceptTag>());

        Assert.Equal(TrendFamily.USChinaModelGap, usChina);
        Assert.Equal(TrendFamily.OpenClosedModelGap, openClosed);
    }
}
