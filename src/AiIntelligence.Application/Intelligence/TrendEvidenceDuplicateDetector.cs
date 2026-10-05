using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Intelligence;

public sealed class TrendEvidenceDuplicateDetector
{
    private const double DefaultDuplicateThreshold = 0.60;
    private const double TopicWeight = 0.20;
    private const double FindingWeight = 0.40;
    private const double EvidenceWeight = 0.25;
    private const double ConceptWeight = 0.15;

    public TrendEvidenceDuplicateDetector(double? duplicateThreshold = null)
    {
        DuplicateThreshold = duplicateThreshold ?? DefaultDuplicateThreshold;
    }

    public double DuplicateThreshold { get; }

    public NormalizedTrendEvidenceCandidate CreateCandidate(
        TrendEvidenceExtractionResult extracted,
        IReadOnlyCollection<AIConceptTag> conceptTags,
        string period,
        TrendEvidencePeriodProvenance periodProvenance,
        Guid sourceItemId,
        Guid sourceDefinitionId,
        TrendFamily trendFamily)
    {
        return CreateCandidate(
            extracted.Topic,
            extracted.Finding,
            extracted.EvidenceSummary,
            extracted.Confidence,
            extracted.SourceUrl,
            extracted.QuantitativeEvidence,
            extracted.PublicationName,
            period,
            periodProvenance,
            conceptTags,
            sourceItemId,
            sourceDefinitionId,
            trendFamily);
    }

    public NormalizedTrendEvidenceCandidate CreateCandidate(TrendEvidence evidence)
    {
        return CreateCandidate(evidence, Guid.Empty, TrendFamily.Unknown);
    }

    public NormalizedTrendEvidenceCandidate CreateCandidate(TrendEvidence evidence, Guid sourceDefinitionId, TrendFamily trendFamily)
    {
        return CreateCandidate(
            evidence.Topic,
            evidence.Finding,
            evidence.EvidenceSummary,
            evidence.Confidence,
            evidence.SourceUrl,
            evidence.QuantitativeEvidence,
            evidence.PublicationName,
            evidence.Period,
            evidence.PeriodProvenance,
            evidence.ConceptTags,
            evidence.SourceItemId,
            sourceDefinitionId,
            trendFamily);
    }

    public bool TryMergeCandidate(
        NormalizedTrendEvidenceCandidate candidate,
        IList<NormalizedTrendEvidenceCandidate> accepted,
        out DuplicateMatch? match)
    {
        match = null;
        for (var index = 0; index < accepted.Count; index++)
        {
            var existing = accepted[index];
            var similarity = EvaluateSimilarity(candidate, existing);
            if (!similarity.IsDuplicate)
            {
                continue;
            }

            var merged = existing.Merge(candidate);
            if (merged.SourceItemId != existing.SourceItemId)
            {
                merged = merged with { SourceItemId = existing.SourceItemId };
            }

            accepted[index] = merged;
            match = new DuplicateMatch(index, similarity, candidate);
            return true;
        }

        return false;
    }

    public IReadOnlyCollection<TrendEvidenceDuplicateGroup> DetectDuplicateGroups(IReadOnlyCollection<TrendEvidence> evidence)
    {
        var groups = new List<TrendEvidenceDuplicateGroup>();
        var bySource = evidence.GroupBy(item => item.SourceItemId);

        foreach (var sourceGroup in bySource)
        {
            var accepted = new List<NormalizedTrendEvidenceCandidate>();
            var duplicates = new Dictionary<Guid, List<TrendEvidenceDuplicateMatch>>();

            foreach (var item in sourceGroup)
            {
                var candidate = CreateCandidate(item);
                if (TryMergeCandidate(candidate, accepted, out var match))
                {
                    var canonical = accepted[match!.MatchedIndex];
                    if (!duplicates.TryGetValue(canonical.CanonicalId, out var list))
                    {
                        list = new List<TrendEvidenceDuplicateMatch>();
                        duplicates[canonical.CanonicalId] = list;
                    }

                    list.Add(new TrendEvidenceDuplicateMatch(match.Duplicate, match.Similarity));
                }
                else
                {
                    accepted.Add(candidate);
                }
            }

            foreach (var entry in duplicates)
            {
                var canonical = accepted.FirstOrDefault(candidate => candidate.CanonicalId == entry.Key);
                if (canonical is null)
                {
                    continue;
                }

                groups.Add(new TrendEvidenceDuplicateGroup(canonical, entry.Value));
            }
        }

        return groups;
    }

    public ConsistencyCheckResult EvaluateConsistency(NormalizedTrendEvidenceCandidate candidate)
    {
        var topicFinding = Jaccard(candidate.TopicTokens, candidate.FindingTokens);
        var topicEvidence = Jaccard(candidate.TopicTokens, candidate.EvidenceTokens);

        if (HasGeoMismatch(candidate))
        {
            return new ConsistencyCheckResult(false, topicFinding, topicEvidence, "Geography mismatch between topic and finding.");
        }

        if (HasOpenClosedMismatch(candidate))
        {
            return new ConsistencyCheckResult(false, topicFinding, topicEvidence, "Open/closed model gap mismatch between topic and finding.");
        }

        if (topicFinding < 0.15 && topicEvidence < 0.15)
        {
            return new ConsistencyCheckResult(false, topicFinding, topicEvidence, "Low topic-to-finding/evidence similarity.");
        }

        return new ConsistencyCheckResult(true, topicFinding, topicEvidence, string.Empty);
    }

    public SimilarityScore EvaluateSimilarity(NormalizedTrendEvidenceCandidate candidate, NormalizedTrendEvidenceCandidate existing)
    {
        if (candidate.SourceDefinitionId != Guid.Empty && existing.SourceDefinitionId != Guid.Empty
            && candidate.SourceDefinitionId != existing.SourceDefinitionId)
        {
            return SimilarityScore.NotDuplicate("Different source definition.");
        }

        var periodSimilarity = PeriodSimilarity(candidate, existing);
        if (periodSimilarity == 0)
        {
            return SimilarityScore.NotDuplicate("Period mismatch.", periodSimilarity);
        }

        if (candidate.TrendFamily != TrendFamily.Unknown
            && existing.TrendFamily != TrendFamily.Unknown
            && candidate.TrendFamily != existing.TrendFamily)
        {
            return SimilarityScore.NotDuplicate("Different trend family.", periodSimilarity);
        }

        var candidateFamily = ExtractCanonicalFamily(candidate.CanonicalTrendKey);
        var existingFamily = ExtractCanonicalFamily(existing.CanonicalTrendKey);
        if (!string.IsNullOrWhiteSpace(candidateFamily)
            && !string.IsNullOrWhiteSpace(existingFamily)
            && !string.Equals(candidateFamily, existingFamily, StringComparison.Ordinal))
        {
            return SimilarityScore.NotDuplicate("Different canonical trend family.", periodSimilarity);
        }

        var topicSimilarity = Jaccard(candidate.TopicTokens, existing.TopicTokens);
        var findingSimilarity = Jaccard(candidate.FindingTokens, existing.FindingTokens);
        var evidenceSimilarity = Jaccard(candidate.EvidenceTokens, existing.EvidenceTokens);
        var conceptSimilarity = Jaccard(candidate.ConceptTagSet, existing.ConceptTagSet);

        var weightedScore = TopicWeight * topicSimilarity
            + FindingWeight * findingSimilarity
            + EvidenceWeight * evidenceSimilarity
            + ConceptWeight * conceptSimilarity;

        var canonicalKeyMatch = !string.IsNullOrWhiteSpace(candidate.CanonicalTrendKey)
            && string.Equals(candidate.CanonicalTrendKey, existing.CanonicalTrendKey, StringComparison.Ordinal);

        var isDuplicate = canonicalKeyMatch || weightedScore >= DuplicateThreshold;
        var reason = canonicalKeyMatch
            ? "Canonical trend key match."
            : weightedScore >= DuplicateThreshold
                ? $"Weighted score {weightedScore:0.00} >= threshold {DuplicateThreshold:0.00}."
                : $"Weighted score {weightedScore:0.00} below threshold {DuplicateThreshold:0.00}.";

        return new SimilarityScore(
            canonicalKeyMatch ? 1.0 : weightedScore,
            topicSimilarity,
            findingSimilarity,
            evidenceSimilarity,
            conceptSimilarity,
            periodSimilarity,
            isDuplicate,
            reason);
    }

    private static bool HasGeoMismatch(NormalizedTrendEvidenceCandidate candidate)
    {
        var topicGeo = ContainsGeoMarker(candidate.RawTopic);
        var findingGeo = ContainsGeoMarker(candidate.RawFinding) || ContainsGeoMarker(candidate.RawEvidenceSummary);
        var findingOpenClosed = ContainsOpenClosedMarker(candidate.RawFinding) || ContainsOpenClosedMarker(candidate.RawEvidenceSummary);

        return topicGeo && !findingGeo && findingOpenClosed;
    }

    private static bool HasOpenClosedMismatch(NormalizedTrendEvidenceCandidate candidate)
    {
        var topicOpenClosed = ContainsOpenClosedMarker(candidate.RawTopic);
        var findingOpenClosed = ContainsOpenClosedMarker(candidate.RawFinding) || ContainsOpenClosedMarker(candidate.RawEvidenceSummary);
        var topicGeo = ContainsGeoMarker(candidate.RawTopic);
        var findingGeo = ContainsGeoMarker(candidate.RawFinding) || ContainsGeoMarker(candidate.RawEvidenceSummary);

        return topicOpenClosed && !findingOpenClosed && findingGeo;
    }

    private static bool ContainsGeoMarker(string value)
    {
        var lower = value.ToLowerInvariant();
        return GeoMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    private static bool ContainsOpenClosedMarker(string value)
    {
        var lower = value.ToLowerInvariant();
        return OpenClosedMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    private static double Jaccard<T>(IReadOnlyCollection<T> left, IReadOnlyCollection<T> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Intersect(right).Count();
        var union = left.Union(right).Count();
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static double PeriodSimilarity(NormalizedTrendEvidenceCandidate candidate, NormalizedTrendEvidenceCandidate existing)
    {
        var left = NormalizePeriod(candidate.BestPeriod);
        var right = NormalizePeriod(existing.BestPeriod);

        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0.5;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        return 0.0;
    }

    private static string NormalizePeriod(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return string.Equals(trimmed, "Unknown", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
    }

    private static string ApplyPhraseReplacements(string value)
    {
        var normalized = value;
        foreach (var (phrase, replacement) in PhraseReplacements)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
            {
                normalized = normalized.Replace(phrase, replacement, StringComparison.Ordinal);
            }
        }

        return normalized;
    }

    private static string ExtractCanonicalFamily(string canonicalKey)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
        {
            return string.Empty;
        }

        var parts = canonicalKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    private static string BuildCanonicalTrendKey(
        Guid sourceDefinitionId,
        string normalizedTopic,
        string normalizedFinding,
        string normalizedEvidence)
    {
        if (sourceDefinitionId == Guid.Empty)
        {
            return string.Empty;
        }

        var normalizedText = string.Join(' ', normalizedTopic, normalizedFinding, normalizedEvidence).Trim();
        var family = CanonicalConceptFamilies
            .FirstOrDefault(entry => normalizedText.Contains(entry.Key, StringComparison.Ordinal)).Value;

        if (string.IsNullOrWhiteSpace(family))
        {
            return string.Empty;
        }

        var keyTokens = normalizedFinding
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Take(6);

        var tokenSuffix = string.Join('-', keyTokens);
        return string.IsNullOrWhiteSpace(tokenSuffix)
            ? $"{sourceDefinitionId:N}:{family}"
            : $"{sourceDefinitionId:N}:{family}:{tokenSuffix}";
    }

    private static NormalizedTrendEvidenceCandidate CreateCandidate(
        string topic,
        string finding,
        string evidenceSummary,
        decimal confidence,
        Uri sourceUrl,
        string quantitativeEvidence,
        string publicationName,
        string period,
        TrendEvidencePeriodProvenance periodProvenance,
        IReadOnlyCollection<AIConceptTag> conceptTags,
        Guid sourceItemId,
        Guid sourceDefinitionId,
        TrendFamily trendFamily)
    {
        var normalizedTopic = NormalizeText(topic);
        var normalizedFinding = NormalizeText(finding);
        var normalizedEvidence = NormalizeText(evidenceSummary);
        var topicTokens = Tokenize(topic);
        var findingTokens = Tokenize(finding);
        var evidenceTokens = Tokenize(evidenceSummary);
        var conceptTagSet = conceptTags.Count == 0
            ? new HashSet<AIConceptTag>()
            : new HashSet<AIConceptTag>(conceptTags);
        var canonicalTrendKey = BuildCanonicalTrendKey(sourceDefinitionId, normalizedTopic, normalizedFinding, normalizedEvidence);

        return new NormalizedTrendEvidenceCandidate(
            Guid.NewGuid(),
            sourceItemId,
            sourceDefinitionId,
            normalizedTopic,
            normalizedFinding,
            normalizedEvidence,
            topicTokens,
            findingTokens,
            evidenceTokens,
            conceptTagSet,
            canonicalTrendKey,
            trendFamily,
            topic,
            period,
            periodProvenance,
            finding,
            evidenceSummary,
            confidence,
            sourceUrl,
            quantitativeEvidence,
            publicationName,
            conceptTags);
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        var buffer = new char[trimmed.Length];
        var index = 0;
        var previousWasSeparator = false;

        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = ch;
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                buffer[index++] = ' ';
                previousWasSeparator = true;
            }
        }

        var normalized = new string(buffer, 0, index).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = ApplyPhraseReplacements(normalized);

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveTrivialSuffix)
            .Where(token => token.Length > 0)
            .ToArray();

        return string.Join(' ', tokens);
    }

    private static HashSet<string> Tokenize(string value)
    {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveTrivialSuffix)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string RemoveTrivialSuffix(string token)
    {
        if (token.Length <= 3)
        {
            return token;
        }

        if (token.EndsWith("ies", StringComparison.Ordinal))
        {
            return token[..^3] + "y";
        }

        if (token.EndsWith("ing", StringComparison.Ordinal) && token.Length > 5)
        {
            token = token[..^3];
        }

        if (token.EndsWith("ation", StringComparison.Ordinal) && token.Length > 7)
        {
            token = token[..^5];
        }
        else if ((token.EndsWith("tion", StringComparison.Ordinal) || token.EndsWith("sion", StringComparison.Ordinal)) && token.Length > 6)
        {
            token = token[..^4];
        }

        if (token.EndsWith("ment", StringComparison.Ordinal) && token.Length > 6)
        {
            token = token[..^4];
        }

        if (token.EndsWith("s", StringComparison.Ordinal) && !token.EndsWith("ss", StringComparison.Ordinal))
        {
            token = token[..^1];
        }

        if (token.EndsWith("a", StringComparison.Ordinal) && token.Length > 5)
        {
            token = token[..^1];
        }

        return token.Length > 5 ? token[..5] : token;
    }

    private static int ScorePeriodSpecificity(string period, TrendEvidencePeriodProvenance provenance)
    {
        if (!string.IsNullOrWhiteSpace(period) && !string.Equals(period, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return provenance switch
            {
                TrendEvidencePeriodProvenance.SourceContent => 4,
                TrendEvidencePeriodProvenance.SourceMetadata => 3,
                TrendEvidencePeriodProvenance.PublicationDate => 2,
                _ => 1
            };
        }

        return 0;
    }

    private static int ScoreEvidenceStrength(NormalizedTrendEvidenceCandidate candidate)
    {
        var evidenceLength = (candidate.RawEvidenceSummary?.Length ?? 0)
            + (candidate.RawFinding?.Length ?? 0)
            + (candidate.BestQuantitativeEvidence?.Length ?? 0);
        return evidenceLength;
    }

    private static int ScoreTopicSpecificity(string topic, IReadOnlyCollection<string> tokens)
    {
        return topic.Length + tokens.Count * 10;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "for", "on", "with", "by", "from", "vs", "versus",
        "about", "into", "over", "under", "between", "among", "across", "per", "through", "via", "using",
        "increase", "increased", "increasing", "improve", "improved", "improving", "growth", "growing", "rise", "rising",
        "trend", "trends", "report", "reports", "study", "studies", "analysis", "summary", "evidence", "finding",
        "impact", "impacts", "effect", "effects", "global", "corporate"
    };

    private static readonly (string Phrase, string Replacement)[] PhraseReplacements =
    {
        ("consumer surplus", "consumer value"),
        ("consumer value", "consumer value"),
        ("adoption pace", "consumer adoption"),
        ("adoption speed", "consumer adoption"),
        ("adoption growth", "consumer adoption"),
        ("adoption among consumers", "consumer adoption"),
        ("task performance gains", "agent task performance"),
        ("task performance", "agent task performance"),
        ("task completion gains", "agent task performance"),
        ("performance convergence", "performance gap closing"),
        ("performance gap closing", "performance gap closing"),
        ("near parity", "performance gap closing"),
        ("open vs closed", "open closed"),
        ("open vs. closed", "open closed"),
        ("closed vs open", "open closed"),
        ("closed vs. open", "open closed"),
        ("investment growth", "investment increase"),
        ("investment surge", "investment increase"),
        ("investment increase", "investment increase"),
        ("benchmark reliability", "benchmark reliability"),
        ("benchmark gaming", "benchmark reliability"),
        ("u s china", "us china"),
        ("u s - china", "us china")
    };

    private static readonly Dictionary<string, string> CanonicalConceptFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["consumer value"] = "ConsumerValue",
        ["consumer adoption"] = "ConsumerAdoption",
        ["open closed"] = "OpenClosedGap",
        ["us china"] = "USChinaModelGap",
        ["agent task performance"] = "AgentTaskPerformance",
        ["benchmark reliability"] = "BenchmarkReliability",
        ["performance gap closing"] = "PerformanceConvergence",
        ["corporate ai investment"] = "CorporateInvestment",
        ["ai investment"] = "CorporateInvestment",
        ["investment increase"] = "CorporateInvestment"
    };

    private static readonly string[] GeoMarkers =
    {
        "u.s", "us", "usa", "china", "europe", "japan", "korea", "india", "uk", "united states", "u s"
    };

    private static readonly string[] OpenClosedMarkers =
    {
        "open", "closed", "open-model", "open model", "closed-model", "closed model"
    };

    public sealed record NormalizedTrendEvidenceCandidate(
        Guid CanonicalId,
        Guid SourceItemId,
        Guid SourceDefinitionId,
        string NormalizedTopic,
        string NormalizedFinding,
        string NormalizedEvidenceSummary,
        HashSet<string> TopicTokens,
        HashSet<string> FindingTokens,
        HashSet<string> EvidenceTokens,
        HashSet<AIConceptTag> ConceptTagSet,
        string CanonicalTrendKey,
        TrendFamily TrendFamily,
        string RawTopic,
        string BestPeriod,
        TrendEvidencePeriodProvenance BestPeriodProvenance,
        string RawFinding,
        string RawEvidenceSummary,
        decimal BestConfidence,
        Uri BestSourceUrl,
        string BestQuantitativeEvidence,
        string BestPublicationName,
        IReadOnlyCollection<AIConceptTag> BestConceptTags)
    {
        public NormalizedTrendEvidenceCandidate Merge(NormalizedTrendEvidenceCandidate candidate)
        {
            var topicScore = ScoreTopicSpecificity(RawTopic, TopicTokens);
            var candidateTopicScore = ScoreTopicSpecificity(candidate.RawTopic, candidate.TopicTokens);
            var summaryScore = RawEvidenceSummary.Length;
            var candidateSummaryScore = candidate.RawEvidenceSummary.Length;
            var periodScore = ScorePeriodSpecificity(BestPeriod, BestPeriodProvenance);
            var candidatePeriodScore = ScorePeriodSpecificity(candidate.BestPeriod, candidate.BestPeriodProvenance);
            var evidenceScore = ScoreEvidenceStrength(this);
            var candidateEvidenceScore = ScoreEvidenceStrength(candidate);

            var preferCandidateTopic = candidateTopicScore > topicScore;
            var preferCandidateSummary = candidateSummaryScore > summaryScore;
            var preferCandidatePeriod = candidatePeriodScore > periodScore;
            var preferCandidateEvidence = candidateEvidenceScore > evidenceScore;

            var preferCandidateConfidence = candidate.BestConfidence > BestConfidence
                && candidateTopicScore == topicScore
                && candidateSummaryScore == summaryScore
                && candidatePeriodScore == periodScore
                && candidateEvidenceScore == evidenceScore;

            var updated = this with
            {
                RawTopic = preferCandidateTopic ? candidate.RawTopic : RawTopic,
                NormalizedTopic = preferCandidateTopic ? candidate.NormalizedTopic : NormalizedTopic,
                TopicTokens = preferCandidateTopic ? candidate.TopicTokens : TopicTokens,
                RawEvidenceSummary = preferCandidateSummary ? candidate.RawEvidenceSummary : RawEvidenceSummary,
                NormalizedEvidenceSummary = preferCandidateSummary ? candidate.NormalizedEvidenceSummary : NormalizedEvidenceSummary,
                EvidenceTokens = preferCandidateSummary ? candidate.EvidenceTokens : EvidenceTokens,
                BestPeriod = preferCandidatePeriod ? candidate.BestPeriod : BestPeriod,
                BestPeriodProvenance = preferCandidatePeriod ? candidate.BestPeriodProvenance : BestPeriodProvenance,
                BestConceptTags = BestConceptTags.Concat(candidate.BestConceptTags).Distinct().ToArray(),
                ConceptTagSet = new HashSet<AIConceptTag>(ConceptTagSet.Union(candidate.ConceptTagSet))
            };

            if (preferCandidateSummary || preferCandidateEvidence)
            {
                updated = updated with
                {
                    RawFinding = candidate.RawFinding,
                    NormalizedFinding = candidate.NormalizedFinding,
                    FindingTokens = candidate.FindingTokens,
                    BestQuantitativeEvidence = candidate.BestQuantitativeEvidence,
                    BestSourceUrl = candidate.BestSourceUrl,
                    BestPublicationName = candidate.BestPublicationName
                };
            }

            if (preferCandidateConfidence)
            {
                updated = updated with { BestConfidence = candidate.BestConfidence };
            }

            return updated;
        }
    }
}

public sealed record TrendEvidenceDuplicateGroup(
    TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate Canonical,
    IReadOnlyCollection<TrendEvidenceDuplicateMatch> Duplicates);

public sealed record TrendEvidenceDuplicateMatch(
    TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate Duplicate,
    SimilarityScore Similarity);

public sealed record DuplicateMatch(
    int MatchedIndex,
    SimilarityScore Similarity,
    TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate Duplicate);

public sealed record SimilarityScore(
    double WeightedScore,
    double TopicSimilarity,
    double FindingSimilarity,
    double EvidenceSimilarity,
    double ConceptSimilarity,
    double PeriodSimilarity,
    bool IsDuplicate,
    string Reason)
{
    public static SimilarityScore NotDuplicate(string reason, double periodSimilarity = 0)
        => new(0, 0, 0, 0, 0, periodSimilarity, false, reason);
}

public sealed record ConsistencyCheckResult(
    bool IsConsistent,
    double TopicFindingSimilarity,
    double TopicEvidenceSimilarity,
    string Reason);
