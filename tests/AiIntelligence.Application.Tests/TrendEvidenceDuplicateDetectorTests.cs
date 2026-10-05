using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Tests;

public sealed class TrendEvidenceDuplicateDetectorTests
{
    private static readonly Guid SourceDefinitionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void TryMergeCandidate_DetectsExactDuplicate()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Agent task completion", "Agents completed more tasks.");
        var second = BuildCandidate(detector, sourceItemId, "Agent task completion", "Agents completed more tasks.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_DoesNotMergeDifferentTrendFamilies()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var agentCandidate = detector.CreateCandidate(
            new TrendEvidenceExtractionResult(
                "AI agent task performance gains",
                "2026",
                TrendEvidencePeriodProvenance.SourceContent,
                "Agents improved OSWorld task completion.",
                "42%",
                "Evidence summary",
                0.8m,
                new Uri("https://example.com"),
                "Example Source",
                new[] { AIConceptTag.ModelCapability }),
            new[] { AIConceptTag.ModelCapability },
            "2026",
            TrendEvidencePeriodProvenance.SourceContent,
            sourceItemId,
            SourceDefinitionId,
            TrendFamily.AgentTaskPerformance);

        var gapCandidate = detector.CreateCandidate(
            new TrendEvidenceExtractionResult(
                "Open vs. closed model performance gap",
                "2026",
                TrendEvidencePeriodProvenance.SourceContent,
                "Closed models regained a lead.",
                "42%",
                "Evidence summary",
                0.8m,
                new Uri("https://example.com"),
                "Example Source",
                new[] { AIConceptTag.ModelCapability }),
            new[] { AIConceptTag.ModelCapability },
            "2026",
            TrendEvidencePeriodProvenance.SourceContent,
            sourceItemId,
            SourceDefinitionId,
            TrendFamily.OpenClosedModelGap);

        candidates.Add(agentCandidate);
        Assert.False(detector.TryMergeCandidate(gapCandidate, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_IgnoresPunctuationAndCase()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Open vs. closed model performance gap", "Closed models lead by 3.3 percent.");
        var second = BuildCandidate(detector, sourceItemId, "Open vs closed model performance gap", "Closed models lead by 3.3 percent");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_HandlesSlightTopicDifferences()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Global corporate AI investment", "Investment doubled year over year.");
        var second = BuildCandidate(detector, sourceItemId, "Global corporate AI investment growth", "Investment doubled year over year.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_HandlesDifferentEvidencePhrasing()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "AI benchmark saturation", "Benchmarks are saturating quickly in 2025.");
        var second = BuildCandidate(detector, sourceItemId, "AI benchmarks saturating quickly", "Benchmark saturation outpacing evaluation design.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_DoesNotMergeDistinctFindingsWithSameTags()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Agent capability improvement", "Agents solve more tasks, but reliability issues persist.");
        var second = BuildCandidate(detector, sourceItemId, "Agent reliability limitations", "Agents fail frequently in long-horizon workflows.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.False(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_CollapsesArenaConvergence()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Top model performance convergence (Elo ratings)", "Top models cluster within 25 Elo points.");
        var second = BuildCandidate(detector, sourceItemId, "Top model performance convergence on Arena Leaderboard", "Top models are within a narrow Elo range.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_CollapsesAgentTaskPerformance()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "AI agent task performance", "Agents improved OSWorld task completion.");
        var second = BuildCandidate(detector, sourceItemId, "Agentic task performance", "OSWorld task completion gains continued.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_CollapsesInvestmentGrowth()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Corporate AI investment growth", "Corporate AI investment doubled year over year.");
        var second = BuildCandidate(detector, sourceItemId, "Global corporate AI investment growth", "Corporate AI investment doubled year over year.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.True(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_DoesNotMergeBenchmarkReliabilityIssues()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Benchmark saturation vs AI capability", "Benchmarks saturate as models improve.");
        var second = BuildCandidate(detector, sourceItemId, "Benchmark reliability issues", "Benchmarks show reliability concerns.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.False(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void TryMergeCandidate_DoesNotMergeInvestmentGeography()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidates = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
        var sourceItemId = Guid.NewGuid();

        var first = BuildCandidate(detector, sourceItemId, "Corporate AI investment growth", "Corporate AI investment doubled year over year.");
        var second = BuildCandidate(detector, sourceItemId, "Geographic concentration of private AI investment", "Investment is concentrated in a few regions.");

        Assert.False(detector.TryMergeCandidate(first, candidates, out _));
        candidates.Add(first);
        Assert.False(detector.TryMergeCandidate(second, candidates, out _));
    }

    [Fact]
    public void EvaluateConsistency_FlagsMismatch()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var candidate = BuildCandidate(detector, Guid.NewGuid(), "U.S.-China model performance gap", "Closed models widened their lead over open models");

        var result = detector.EvaluateConsistency(candidate);

        Assert.False(result.IsConsistent);
        Assert.Contains("mismatch", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectDuplicateGroups_IsIdempotent()
    {
        var detector = new TrendEvidenceDuplicateDetector();
        var sourceItemId = Guid.NewGuid();
        var evidence = new[]
        {
            BuildEvidence(sourceItemId, "Organizational AI adoption", "Adoption increased to 78%.", "2026"),
            BuildEvidence(sourceItemId, "Organizational AI adoption", "Adoption increased to 78 percent.", "2026"),
            BuildEvidence(sourceItemId, "Organizational AI adoption", "Adoption increased to 78%.", "2026")
        };

        var groups = detector.DetectDuplicateGroups(evidence);
        var groupsSecondRun = detector.DetectDuplicateGroups(evidence);

        Assert.Equal(groups.Count, groupsSecondRun.Count);
        Assert.Single(groups);
    }

    private static TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate BuildCandidate(
        TrendEvidenceDuplicateDetector detector,
        Guid sourceItemId,
        string topic,
        string finding)
    {
        var extracted = new TrendEvidenceExtractionResult(
            topic,
            "2026",
            TrendEvidencePeriodProvenance.SourceContent,
            finding,
            "42%",
            "Evidence summary",
            0.8m,
            new Uri("https://example.com"),
            "Example Source",
            new[] { AIConceptTag.ModelCapability, AIConceptTag.Evaluation });

        return detector.CreateCandidate(
            extracted,
            extracted.ConceptTags,
            "2026",
            TrendEvidencePeriodProvenance.SourceContent,
            sourceItemId,
            SourceDefinitionId,
            TrendFamily.AgentTaskPerformance);
    }

    private static TrendEvidence BuildEvidence(Guid sourceItemId, string topic, string finding, string period)
    {
        return new TrendEvidence(
            Guid.NewGuid(),
            sourceItemId,
            topic,
            period,
            TrendEvidencePeriodProvenance.SourceContent,
            finding,
            "Evidence summary",
            0.8m,
            new Uri("https://example.com"),
            "42%",
            "Example Source",
            new[] { AIConceptTag.ModelCapability, AIConceptTag.Evaluation });
    }
}
