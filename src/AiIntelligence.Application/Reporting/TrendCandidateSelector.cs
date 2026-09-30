using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class TrendCandidateSelector : ITrendCandidateSelector
{
    private static readonly IReadOnlyCollection<string> StopWords = new[] { "the", "and", "for", "with", "unknown", "from", "into" };
    private static readonly IReadOnlyCollection<string> ProductKeywords = new[] { "sdk", "framework", "platform", "api", "tooling", "model" };
    private readonly ITrendEvidenceRepository _trendEvidenceRepository;
    private readonly ILogger<TrendCandidateSelector> _logger;

    public TrendCandidateSelector(ITrendEvidenceRepository trendEvidenceRepository, ILogger<TrendCandidateSelector> logger)
    {
        _trendEvidenceRepository = trendEvidenceRepository;
        _logger = logger;
    }

    public async Task<TrendCandidateSelection> SelectCandidatesAsync(
        IntelligenceItem intelligenceItem,
        int maxCandidates,
        CancellationToken cancellationToken)
    {
        var trends = await _trendEvidenceRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var queryTokens = Tokenize(string.Join(' ', intelligenceItem.Topic, intelligenceItem.Category, intelligenceItem.ProductOrFramework, intelligenceItem.Vendor));
        if (queryTokens.Count == 0)
        {
            return new TrendCandidateSelection(Array.Empty<TrendEvidence>(), Array.Empty<TrendCandidateDiagnostic>());
        }

        var max = Math.Clamp(maxCandidates, 1, 5);
        var scored = trends.Select(trend => ScoreCandidate(intelligenceItem, queryTokens, trend)).Where(candidate => candidate.Score > 0).ToArray();
        if (scored.Length == 0)
        {
            return new TrendCandidateSelection(Array.Empty<TrendEvidence>(), Array.Empty<TrendCandidateDiagnostic>());
        }

        var minimumScore = Math.Max(2, queryTokens.Count >= 4 ? 3 : 2);
        var filtered = scored.Where(candidate => candidate.Score >= minimumScore).ToArray();
        if (filtered.Length == 0)
        {
            return new TrendCandidateSelection(Array.Empty<TrendEvidence>(), Array.Empty<TrendCandidateDiagnostic>());
        }

        scored = filtered;

        var independent = scored.Where(candidate => !candidate.IsSameSource).OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Trend.Confidence)
            .ToArray();
        var sameSource = scored.Where(candidate => candidate.IsSameSource).OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Trend.Confidence)
            .ToArray();

        var selected = new List<ScoredTrendCandidate>();
        selected.AddRange(independent.Take(max));

        if (selected.Count == 0)
        {
            selected.AddRange(sameSource.Take(max));
        }

        var finalSelection = selected.Take(max).ToArray();
        var diagnostics = BuildDiagnostics(independent, sameSource, finalSelection);
        foreach (var candidate in diagnostics)
        {
            _logger.LogInformation(
                "Candidate: Topic={Topic}; Source={Source}; URL={Url}; MatchReason={MatchReason}; SameSource={SameSource}; Score={Score}; Selected={Selected}",
                candidate.Topic,
                candidate.SourceName,
                candidate.SourceUrl,
                candidate.MatchReason,
                candidate.SameSourceEvidence ? "Yes" : "No",
                candidate.Score,
                candidate.Selected ? "Yes" : "No");
        }

        return new TrendCandidateSelection(finalSelection.Select(candidate => candidate.Trend).ToArray(), diagnostics);
    }

    private static ScoredTrendCandidate ScoreCandidate(IntelligenceItem intelligenceItem, IReadOnlyCollection<string> queryTokens, TrendEvidence trend)
    {
        var trendTokens = Tokenize(string.Join(' ', trend.Topic, trend.Finding, trend.EvidenceSummary));
        var topicScore = Tokenize(intelligenceItem.Topic).Count(token => trendTokens.Contains(token));
        var categoryScore = Tokenize(intelligenceItem.Category).Count(token => trendTokens.Contains(token));
        var productScore = ScoreProductRelevance(intelligenceItem.ProductOrFramework, trendTokens);
        var periodScore = trend.Period.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        var totalScore = (topicScore * 4) + (categoryScore * 2) + (productScore * 2) + periodScore;

        var matchReason = string.Join("; ", new[]
        {
            topicScore > 0 ? $"topic match ({topicScore})" : null,
            categoryScore > 0 ? $"category match ({categoryScore})" : null,
            productScore > 0 ? $"product relevance ({productScore})" : null,
            periodScore > 0 ? "period available" : null
        }.Where(reason => reason is not null));
        matchReason = string.IsNullOrWhiteSpace(matchReason) ? "no strong lexical match" : matchReason;

        var isSameSource = trend.SourceUrl == intelligenceItem.SourceUrl;
        return new ScoredTrendCandidate(trend, totalScore, matchReason, isSameSource);
    }

    private static int ScoreProductRelevance(string productOrFramework, IReadOnlyCollection<string> trendTokens)
    {
        if (string.IsNullOrWhiteSpace(productOrFramework))
        {
            return 0;
        }

        var productTokens = Tokenize(productOrFramework);
        var keywordMatches = productTokens.Count(token => ProductKeywords.Contains(token));
        var tokenMatches = productTokens.Count(token => trendTokens.Contains(token));
        return Math.Clamp(tokenMatches + keywordMatches, 0, 2);
    }

    private static IReadOnlySet<string> Tokenize(string value)
    {
        return value
            .Split(new[] { ' ', '-', '/', '.', ',', ':', ';', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(token => token.Length >= 3)
            .Except(StopWords)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<TrendCandidateDiagnostic> BuildDiagnostics(
        IReadOnlyCollection<ScoredTrendCandidate> independent,
        IReadOnlyCollection<ScoredTrendCandidate> sameSource,
        IReadOnlyCollection<ScoredTrendCandidate> selected)
    {
        var ranked = independent.Concat(sameSource).Select((candidate, index) => new { candidate, rank = index + 1 }).ToArray();
        return ranked.Select(entry =>
        {
            var candidate = entry.candidate;
            var isSelected = selected.Any(item => item.Trend.Id == candidate.Trend.Id);
            var rejectionReason = isSelected
                ? null
                : candidate.IsSameSource && independent.Count > 0
                    ? "Excluded due to same-source evidence with independent candidates available."
                    : "Lower score or limited relevance.";

            return new TrendCandidateDiagnostic(
                candidate.Trend.Id,
                candidate.Trend.Topic,
                candidate.Trend.Period,
                string.IsNullOrWhiteSpace(candidate.Trend.PublicationName) ? "Unknown" : candidate.Trend.PublicationName,
                candidate.Trend.SourceUrl,
                candidate.IsSameSource,
                candidate.MatchReason,
                candidate.Score,
                entry.rank,
                isSelected,
                rejectionReason);
        }).ToArray();
    }

    private sealed record ScoredTrendCandidate(TrendEvidence Trend, int Score, string MatchReason, bool IsSameSource);
}
