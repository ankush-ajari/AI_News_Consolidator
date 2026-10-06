using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class TrendCandidateSelector : ITrendCandidateSelector
{
    private static readonly IReadOnlyCollection<string> StopWords = new[] { "the", "and", "for", "with", "unknown", "from", "into" };
    private static readonly IReadOnlyCollection<string> ProductKeywords = new[] { "sdk", "framework", "platform", "api", "tooling", "model" };
    private static readonly IReadOnlyCollection<TrendEvidencePeriodProvenance> PeriodProvenanceOrder = new[]
    {
        TrendEvidencePeriodProvenance.SourceContent,
        TrendEvidencePeriodProvenance.SourceMetadata,
        TrendEvidencePeriodProvenance.PublicationDate,
        TrendEvidencePeriodProvenance.Unknown
    };
    private static readonly IReadOnlyDictionary<AIConceptTag, int> ConceptSpecificityWeights = new Dictionary<AIConceptTag, int>
    {
        [AIConceptTag.AgenticAI] = 2,
        [AIConceptTag.ToolUse] = 2,
        [AIConceptTag.SpeechVoice] = 2,
        [AIConceptTag.MultimodalAI] = 2,
        [AIConceptTag.ModelOperations] = 2,
        [AIConceptTag.Evaluation] = 2,
        [AIConceptTag.ModelCapability] = 1,
        [AIConceptTag.DeveloperPlatform] = 1,
        [AIConceptTag.EnterpriseAdoption] = 1,
        [AIConceptTag.WorkforceImpact] = 1,
        [AIConceptTag.AIInvestment] = 1,
        [AIConceptTag.Productivity] = 1,
        [AIConceptTag.LocalEdgeAI] = 1,
        [AIConceptTag.OpenSourceModels] = 1,
        [AIConceptTag.ModelCompetition] = 1,
        [AIConceptTag.ResponsibleAI] = 1,
        [AIConceptTag.Governance] = 1
    };
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
        var scored = trends.Select(trend => ScoreCandidate(intelligenceItem, queryTokens, trend)).ToArray();
        var eligible = scored.Where(candidate => candidate.Eligible).ToArray();
        if (eligible.Length == 0)
        {
            return new TrendCandidateSelection(Array.Empty<TrendEvidence>(), BuildDiagnostics(scored, Array.Empty<ScoredTrendCandidate>(), Array.Empty<ScoredTrendCandidate>()));
        }

        var minimumScore = Math.Max(2, queryTokens.Count >= 4 ? 3 : 2);
        var filtered = eligible.Where(candidate => candidate.Score >= minimumScore).ToArray();
        if (filtered.Length == 0)
        {
            return new TrendCandidateSelection(Array.Empty<TrendEvidence>(), BuildDiagnostics(scored, Array.Empty<ScoredTrendCandidate>(), Array.Empty<ScoredTrendCandidate>()));
        }

        var ranked = filtered.OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Trend.Confidence)
            .ThenByDescending(candidate => candidate.IndependentSourceScore)
            .ToArray();

        var finalSelection = ranked.Take(max).ToArray();
        var diagnostics = BuildDiagnostics(scored, ranked, finalSelection);
        foreach (var candidate in diagnostics)
        {
            _logger.LogInformation(
                "Candidate: Topic={Topic}; Source={Source}; URL={Url}; Family={Family}; Period={Period}({Provenance}); Confidence={Confidence:0.##}; MatchReason={MatchReason}; SameSource={SameSource}; Score={Score}; Selected={Selected}; Eligible={Eligible}; Eligibility={Eligibility}; Topic={TopicCount}; Category={CategoryCount}; Product={ProductScore}; Concept={ConceptCount}; SpecificConcept={SpecificConcept}; PeriodScore={PeriodScore}; ConfidenceScore={ConfidenceScore}; FamilyCompatibility={FamilyScore}; IndependentScore={IndependentScore}",
                candidate.Topic,
                candidate.SourceName,
                candidate.SourceUrl,
                candidate.TrendFamily,
                candidate.Period,
                candidate.PeriodProvenance,
                candidate.Confidence,
                candidate.MatchReason,
                candidate.SameSourceEvidence ? "Yes" : "No",
                candidate.Score,
                candidate.Selected ? "Yes" : "No",
                candidate.Eligible ? "Yes" : "No",
                candidate.EligibilityReason,
                candidate.TopicMatchCount,
                candidate.CategoryMatchCount,
                candidate.ProductScore,
                candidate.ConceptMatchCount,
                candidate.SpecificConceptScore,
                candidate.PeriodScore,
                candidate.ConfidenceScore,
                candidate.FamilyCompatibilityScore,
                candidate.IndependentSourceScore);
        }

        return new TrendCandidateSelection(finalSelection.Select(candidate => candidate.Trend).ToArray(), diagnostics);
    }

    private static ScoredTrendCandidate ScoreCandidate(IntelligenceItem intelligenceItem, IReadOnlyCollection<string> queryTokens, TrendEvidence trend)
    {
        var trendTokens = Tokenize(string.Join(' ', trend.Topic, trend.Finding, trend.EvidenceSummary));
        var topicScore = Tokenize(intelligenceItem.Topic).Count(token => trendTokens.Contains(token));
        var categoryScore = Tokenize(intelligenceItem.Category).Count(token => trendTokens.Contains(token));
        var productScore = ScoreProductRelevance(intelligenceItem.ProductOrFramework, trendTokens);
        var conceptMatchTags = intelligenceItem.ConceptTags.Intersect(trend.ConceptTags).Distinct().ToArray();
        var conceptMatchCount = conceptMatchTags.Length;
        var specificConceptScore = ScoreConceptSpecificity(conceptMatchTags);
        var periodScore = ScorePeriodMetadata(trend);
        var confidenceScore = ScoreConfidence(trend.Confidence);
        var hasAgentSignals = HasAgentSignals(intelligenceItem);
        var familyCompatibilityScore = ScoreFamilyCompatibility(intelligenceItem.ConceptTags, trend.TrendFamily, hasAgentSignals);
        var independentSourceScore = trend.SourceUrl == intelligenceItem.SourceUrl ? 0 : 1;
        var productTokens = Tokenize(intelligenceItem.ProductOrFramework);
        var hasSpecificProductOverlap = productTokens.Except(ProductKeywords).Any(token => trendTokens.Contains(token));
        var strongLexicalMatch = topicScore >= 2 || hasSpecificProductOverlap || categoryScore >= 2;
        var hasSpecificConceptMatch = specificConceptScore > 0;
        var hasCompatibleFamily = familyCompatibilityScore > 0;
        var hasSupportingSignal = (topicScore + categoryScore + conceptMatchCount) > 0 || hasSpecificProductOverlap;
        var familyOnlyEligible = AllowsFamilyOnlyEligibility(intelligenceItem.ConceptTags, trend.TrendFamily);
        var eligible = strongLexicalMatch || hasSpecificConceptMatch || (hasCompatibleFamily && hasSupportingSignal) || familyOnlyEligible;
        var eligibilityReason = eligible
            ? string.Join("; ", new[]
            {
                strongLexicalMatch ? "strong lexical match" : null,
                hasSpecificConceptMatch ? "specific concept overlap" : null,
                hasCompatibleFamily && hasSupportingSignal ? "compatible family with supporting signal" : null,
                familyOnlyEligible ? "family-only eligibility" : null
            }.Where(reason => reason is not null))
            : "No strong lexical, concept, or supported family compatibility signal";
        var penalty = (!strongLexicalMatch && !hasSpecificConceptMatch && !hasCompatibleFamily && hasSupportingSignal) ? 2 : 0;
        var totalScore = eligible
            ? (topicScore * 4)
              + (categoryScore * 2)
              + (productScore * 2)
              + (conceptMatchCount * 1)
              + (specificConceptScore * 2)
              + periodScore
              + confidenceScore
              + familyCompatibilityScore
              - penalty
            : 0;

        var matchReason = string.Join("; ", new[]
        {
            topicScore > 0 ? $"topic match ({topicScore})" : null,
            categoryScore > 0 ? $"category match ({categoryScore})" : null,
            productScore > 0 ? $"product relevance ({productScore})" : null,
            conceptMatchCount > 0 ? $"concept match ({conceptMatchCount})" : null,
            specificConceptScore > 0 ? $"specific concept weight ({specificConceptScore})" : null,
            periodScore > 0 ? $"period tie-break ({periodScore})" : null,
            confidenceScore > 0 ? $"confidence weight ({confidenceScore})" : null,
            familyCompatibilityScore > 0 ? "compatible trend family" : null,
            penalty > 0 ? $"weak match penalty (-{penalty})" : null
        }.Where(reason => reason is not null));
        matchReason = string.IsNullOrWhiteSpace(matchReason)
            ? "no strong lexical match"
            : matchReason;

        var isSameSource = trend.SourceUrl == intelligenceItem.SourceUrl;
        return new ScoredTrendCandidate(
            trend,
            totalScore,
            matchReason,
            isSameSource,
            topicScore,
            categoryScore,
            productScore,
            conceptMatchCount,
            specificConceptScore,
            periodScore,
            confidenceScore,
            familyCompatibilityScore,
            independentSourceScore,
            eligible,
            eligibilityReason)
        {
            IntelligenceConceptTags = intelligenceItem.ConceptTags
        };
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

    private static int ScoreConceptSpecificity(IEnumerable<AIConceptTag> conceptTags)
    {
        var score = 0;
        foreach (var tag in conceptTags)
        {
            score += ConceptSpecificityWeights.TryGetValue(tag, out var weight) ? weight : 1;
        }

        return score;
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
        IReadOnlyCollection<ScoredTrendCandidate> scored,
        IReadOnlyCollection<ScoredTrendCandidate> ranked,
        IReadOnlyCollection<ScoredTrendCandidate> selected)
    {
        var rankedLookup = ranked.Select((candidate, index) => new { candidate, rank = index + 1 })
            .ToDictionary(item => item.candidate.Trend.Id, item => item.rank);
        return scored.Select(candidate =>
        {
            rankedLookup.TryGetValue(candidate.Trend.Id, out var rank);
            var isSelected = selected.Any(item => item.Trend.Id == candidate.Trend.Id);
            var rejectionReason = isSelected
                ? null
                : candidate.Eligible
                    ? "Lower score or limited relevance."
                    : "Rejected by semantic eligibility gate.";

            var conceptMatches = candidate.TopicConceptMatches == 0
                ? "(none)"
                : string.Join(", ", candidate.Trend.ConceptTags.Intersect(candidate.IntelligenceConceptTags).Distinct());

            return new TrendCandidateDiagnostic(
                candidate.Trend.Id,
                candidate.Trend.Topic,
                candidate.Trend.Period,
                candidate.Trend.PeriodProvenance,
                string.IsNullOrWhiteSpace(candidate.Trend.PublicationName) ? "Unknown" : candidate.Trend.PublicationName,
                candidate.Trend.SourceUrl,
                candidate.Trend.TrendFamily,
                candidate.Trend.Confidence,
                candidate.IsSameSource,
                candidate.MatchReason,
                candidate.Score,
                rank,
                isSelected,
                rejectionReason,
                candidate.Eligible,
                candidate.EligibilityReason,
                candidate.TopicMatchCount,
                candidate.CategoryMatchCount,
                candidate.ProductScore,
                candidate.TopicConceptMatches,
                candidate.SpecificConceptScore,
                conceptMatches,
                candidate.PeriodScore,
                candidate.ConfidenceScore,
                candidate.FamilyCompatibilityScore,
                candidate.IndependentSourceScore);
        }).ToArray();
    }

    private static int ScorePeriodMetadata(TrendEvidence trend)
    {
        if (string.IsNullOrWhiteSpace(trend.Period) || trend.Period.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return 1;
    }

    private static int ScoreConfidence(decimal confidence)
    {
        if (confidence >= 0.85m)
        {
            return 2;
        }

        return confidence >= 0.7m ? 1 : 0;
    }

    private static bool AllowsFamilyOnlyEligibility(IReadOnlyCollection<AIConceptTag> conceptTags, TrendFamily trendFamily)
    {
        return trendFamily switch
        {
            TrendFamily.AgentTaskPerformance => conceptTags.Contains(AIConceptTag.AgenticAI) || conceptTags.Contains(AIConceptTag.ToolUse),
            TrendFamily.AgentReliability => conceptTags.Contains(AIConceptTag.AgenticAI) || conceptTags.Contains(AIConceptTag.ToolUse),
            TrendFamily.BenchmarkReliability => conceptTags.Contains(AIConceptTag.Evaluation),
            _ => false
        };
    }

    private static int ScoreFamilyCompatibility(IReadOnlyCollection<AIConceptTag> conceptTags, TrendFamily trendFamily, bool hasAgentSignals)
    {
        if (trendFamily == TrendFamily.Unknown)
        {
            return 0;
        }

        return trendFamily switch
        {
            TrendFamily.AgentTaskPerformance => conceptTags.Contains(AIConceptTag.AgenticAI) || conceptTags.Contains(AIConceptTag.ToolUse) ? (hasAgentSignals ? 3 : 2) : 0,
            TrendFamily.AgentReliability => conceptTags.Contains(AIConceptTag.AgenticAI) || conceptTags.Contains(AIConceptTag.ToolUse) ? (hasAgentSignals ? 3 : 2) : 0,
            TrendFamily.BenchmarkReliability => conceptTags.Contains(AIConceptTag.Evaluation) ? 2 : 0,
            TrendFamily.BenchmarkSaturation => conceptTags.Contains(AIConceptTag.Evaluation) ? 2 : 0,
            TrendFamily.ModelPerformanceConvergence => conceptTags.Contains(AIConceptTag.ModelCompetition) || conceptTags.Contains(AIConceptTag.Evaluation) ? 2 : 0,
            TrendFamily.OpenClosedModelGap => conceptTags.Contains(AIConceptTag.OpenSourceModels) || conceptTags.Contains(AIConceptTag.ModelCompetition) ? 2 : 0,
            TrendFamily.USChinaModelGap => conceptTags.Contains(AIConceptTag.ModelCompetition) ? 2 : 0,
            TrendFamily.ComputeInfrastructureSpend => conceptTags.Contains(AIConceptTag.ModelOperations) ? 2 : 0,
            TrendFamily.OrganizationalAIAdoption => conceptTags.Contains(AIConceptTag.EnterpriseAdoption) || conceptTags.Contains(AIConceptTag.AgenticAI) || conceptTags.Contains(AIConceptTag.ToolUse) ? (hasAgentSignals ? 3 : 2) : 0,
            TrendFamily.CorporateAIInvestment => conceptTags.Contains(AIConceptTag.AIInvestment) ? 2 : 0,
            TrendFamily.GeographicAIInvestment => conceptTags.Contains(AIConceptTag.AIInvestment) ? 2 : 0,
            TrendFamily.AIProductivity => conceptTags.Contains(AIConceptTag.Productivity) ? 2 : 0,
            TrendFamily.WorkforceImpact => conceptTags.Contains(AIConceptTag.WorkforceImpact) ? 2 : 0,
            TrendFamily.ConsumerAIAdoption => conceptTags.Contains(AIConceptTag.EnterpriseAdoption) ? 1 : 0,
            TrendFamily.ConsumerAIValue => conceptTags.Contains(AIConceptTag.EnterpriseAdoption) ? 1 : 0,
            TrendFamily.RoboticsRealWorldGap => conceptTags.Contains(AIConceptTag.LocalEdgeAI) ? 1 : 0,
            TrendFamily.AutonomousVehicleDeployment => conceptTags.Contains(AIConceptTag.LocalEdgeAI) ? 1 : 0,
            TrendFamily.JaggedIntelligence => conceptTags.Contains(AIConceptTag.ModelCompetition) || conceptTags.Contains(AIConceptTag.Evaluation) ? 1 : 0,
            _ => 0
        };
    }

    private static bool HasAgentSignals(IntelligenceItem intelligenceItem)
    {
        var topicTokens = Tokenize(intelligenceItem.Topic);
        var categoryTokens = Tokenize(intelligenceItem.Category);
        var productTokens = Tokenize(intelligenceItem.ProductOrFramework);
        return intelligenceItem.ConceptTags.Contains(AIConceptTag.AgenticAI)
            || intelligenceItem.ConceptTags.Contains(AIConceptTag.ToolUse)
            || topicTokens.Contains("agent")
            || categoryTokens.Contains("agent")
            || productTokens.Contains("agent");
    }

    private sealed record ScoredTrendCandidate(
        TrendEvidence Trend,
        int Score,
        string MatchReason,
        bool IsSameSource,
        int TopicMatchCount,
        int CategoryMatchCount,
        int ProductScore,
        int TopicConceptMatches,
        int SpecificConceptScore,
        int PeriodScore,
        int ConfidenceScore,
        int FamilyCompatibilityScore,
        int IndependentSourceScore,
        bool Eligible,
        string EligibilityReason)
    {
        public IReadOnlyCollection<AIConceptTag> IntelligenceConceptTags { get; init; } = Array.Empty<AIConceptTag>();
    }
}
