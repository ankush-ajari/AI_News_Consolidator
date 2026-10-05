using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Intelligence;

public sealed class TrendAnalysisService
{
    private readonly IRawSourceRepository _rawSourceRepository;
    private readonly ISourceDefinitionRepository _sourceDefinitionRepository;
    private readonly ITrendEvidenceRepository _trendEvidenceRepository;
    private readonly ITrendEvidenceExtractor _extractor;
    private readonly IAIConceptClassifier _conceptClassifier;
    private readonly ITrendFamilyClassifier _trendFamilyClassifier;
    private readonly ILogger<TrendAnalysisService> _logger;
    private readonly TrendEvidenceDuplicateDetector _duplicateDetector;

    public TrendAnalysisService(
        IRawSourceRepository rawSourceRepository,
        ISourceDefinitionRepository sourceDefinitionRepository,
        ITrendEvidenceRepository trendEvidenceRepository,
        ITrendEvidenceExtractor extractor,
        ILogger<TrendAnalysisService> logger)
        : this(rawSourceRepository, sourceDefinitionRepository, trendEvidenceRepository, extractor, new DeterministicAIConceptClassifier(), logger)
    {
    }

    public TrendAnalysisService(
        IRawSourceRepository rawSourceRepository,
        ISourceDefinitionRepository sourceDefinitionRepository,
        ITrendEvidenceRepository trendEvidenceRepository,
        ITrendEvidenceExtractor extractor,
        IAIConceptClassifier conceptClassifier,
        ILogger<TrendAnalysisService> logger)
    {
        _rawSourceRepository = rawSourceRepository;
        _sourceDefinitionRepository = sourceDefinitionRepository;
        _trendEvidenceRepository = trendEvidenceRepository;
        _extractor = extractor;
        _conceptClassifier = conceptClassifier;
        _trendFamilyClassifier = new DeterministicTrendFamilyClassifier();
        _logger = logger;
        _duplicateDetector = new TrendEvidenceDuplicateDetector();
    }

    public async Task<TrendAnalysisResult> AnalyzeTrendResearchAsync(CancellationToken cancellationToken, int? limit = null)
    {
        var rawItems = await _rawSourceRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var remainingLimit = limit is > 0 ? limit.Value : (int?)null;
        var processedCount = 0;
        var persistedCount = 0;
        var skippedNonTrendResearchCount = 0;
        var failedCount = 0;
        var duplicateGroupsDetected = 0;
        var duplicatesSuppressed = 0;

        var acceptedBySource = new Dictionary<Guid, List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>>();
        var stagedBySource = new Dictionary<Guid, List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>>();

        foreach (var rawItem in rawItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceDefinition = await _sourceDefinitionRepository.GetByIdAsync(rawItem.SourceDefinitionId, cancellationToken).ConfigureAwait(false);
            var sourceClass = sourceDefinition?.SourceClass ?? rawItem.SourceClass;
            if (sourceClass != SourceClass.TrendResearch)
            {
                skippedNonTrendResearchCount++;
                continue;
            }

            if (await _trendEvidenceRepository.ExistsForSourceItemAsync(rawItem.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (remainingLimit == 0)
            {
                break;
            }

            if (remainingLimit.HasValue)
            {
                remainingLimit--;
            }

            processedCount++;
            try
            {
                if (sourceDefinition is null)
                {
                    _logger.LogWarning("Missing source definition for trend research raw source item {SourceItemId}; using source class {SourceClass}.", rawItem.Id, rawItem.SourceClass);
                }

                var sourceDefinitionId = sourceDefinition?.Id ?? rawItem.SourceDefinitionId;
                if (!stagedBySource.TryGetValue(sourceDefinitionId, out var staged))
                {
                    staged = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
                    stagedBySource[sourceDefinitionId] = staged;
                }

                var extractionBatch = await _extractor.ExtractAsync(rawItem, cancellationToken).ConfigureAwait(false);
                foreach (var extracted in extractionBatch.Items)
                {
                    var periodResolution = TrendEvidencePeriodResolver.Resolve(extracted, rawItem, sourceDefinition);
                    var conceptTags = _conceptClassifier.Classify(
                        rawItem.Title,
                        extracted.Topic,
                        string.Empty,
                        string.Empty,
                        extracted.Finding);

                    var trendFamily = _trendFamilyClassifier.Classify(
                        extracted.Topic,
                        extracted.Finding,
                        extracted.EvidenceSummary,
                        conceptTags);

                    var candidate = _duplicateDetector.CreateCandidate(
                        extracted,
                        conceptTags,
                        periodResolution.Period,
                        periodResolution.Provenance,
                        rawItem.Id,
                        sourceDefinitionId,
                        trendFamily);

                    if (candidate.TrendFamily == TrendFamily.Unknown)
                    {
                        _logger.LogWarning(
                            "Trend family classified as Unknown for raw source item {SourceItemId}. Topic: {Topic}. SourceDefinition: {SourceDefinitionId}.",
                            rawItem.Id,
                            candidate.RawTopic,
                            sourceDefinitionId);
                    }

                    _logger.LogInformation(
                        "Trend candidate before consolidation. Topic: {Topic}. TrendFamily: {TrendFamily}. ConceptTags: {ConceptTags}. SourceDefinition: {SourceDefinitionId}.",
                        candidate.RawTopic,
                        candidate.TrendFamily,
                        candidate.BestConceptTags.Count == 0 ? "(none)" : string.Join(", ", candidate.BestConceptTags),
                        sourceDefinitionId);

                    var consistency = _duplicateDetector.EvaluateConsistency(candidate);
                    if (!consistency.IsConsistent)
                    {
                        _logger.LogWarning(
                            "Skipping inconsistent trend evidence for raw source item {SourceItemId}. Topic: {Topic}. Reason: {Reason}",
                            rawItem.Id,
                            candidate.RawTopic,
                            consistency.Reason);
                        continue;
                    }

                    staged.Add(candidate);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedCount++;
                _logger.LogError(exception, "Failed to analyze trend research raw source item {SourceItemId}.", rawItem.Id);
            }
        }

        foreach (var entry in stagedBySource)
        {
            var accepted = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
            var suppressedInGroup = 0;
            var groupedByFamily = entry.Value
                .GroupBy(candidate => candidate.TrendFamily)
                .OrderBy(group => group.Key == TrendFamily.Unknown ? 1 : 0)
                .ThenBy(group => group.Key.ToString())
                .ToArray();

            foreach (var familyGroup in groupedByFamily)
            {
                var familyCandidates = familyGroup.ToList();
                familyCandidates.Sort(CompareCandidateSpecificity);
                var familyAccepted = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();

                foreach (var candidate in familyCandidates)
                {
                    if (candidate.TrendFamily == TrendFamily.Unknown)
                    {
                        var specificMatch = accepted.FirstOrDefault(existing =>
                            existing.TrendFamily != TrendFamily.Unknown
                            && _duplicateDetector.EvaluateSimilarity(candidate, existing).IsDuplicate);

                        if (specificMatch is not null)
                        {
                            suppressedInGroup++;
                            duplicatesSuppressed++;
                            _logger.LogInformation(
                                "Suppressing Unknown family candidate in favor of specific family {TrendFamily}. Topic: {Topic}.",
                                specificMatch.TrendFamily,
                                candidate.RawTopic);
                            continue;
                        }
                    }

                    if (candidate.TrendFamily == TrendFamily.Unknown)
                    {
                        if (_duplicateDetector.TryMergeCandidate(candidate, familyAccepted, out var unknownMatch))
                        {
                            suppressedInGroup++;
                            duplicatesSuppressed++;
                            _logger.LogInformation(
                                "Skipping near-duplicate Unknown trend evidence for source definition {SourceDefinitionId}. Topic: {Topic}. Score: {Score:0.00}",
                                entry.Key,
                                candidate.RawTopic,
                                unknownMatch?.Similarity.WeightedScore ?? 0);
                            continue;
                        }
                    }
                    else if (_duplicateDetector.TryMergeCandidate(candidate, familyAccepted, out var match))
                    {
                        suppressedInGroup++;
                        duplicatesSuppressed++;
                        _logger.LogInformation(
                            "Skipping near-duplicate trend evidence for source definition {SourceDefinitionId} family {TrendFamily}. Topic: {Topic}. Score: {Score:0.00}",
                            entry.Key,
                            candidate.TrendFamily,
                            candidate.RawTopic,
                            match?.Similarity.WeightedScore ?? 0);
                        continue;
                    }

                    familyAccepted.Add(candidate);
                }

                if (familyGroup.Key == TrendFamily.Unknown)
                {
                    if (familyAccepted.Count > 0)
                    {
                        _logger.LogWarning(
                            "Unknown trend family candidates retained for source definition {SourceDefinitionId}: {Count}.",
                            entry.Key,
                            familyAccepted.Count);
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "Consolidation group. SourceDefinition: {SourceDefinitionId}. TrendFamily: {TrendFamily}. Candidate count: {CandidateCount}. Canonical: {CanonicalTopic}. Suppressed: {Suppressed}.",
                        entry.Key,
                        familyGroup.Key,
                        familyCandidates.Count,
                        familyAccepted.FirstOrDefault()?.RawTopic ?? "(none)",
                        familyCandidates.Count - familyAccepted.Count);
                }

                accepted.AddRange(familyAccepted);
            }

            if (suppressedInGroup > 0)
            {
                duplicateGroupsDetected++;
            }

            acceptedBySource[entry.Key] = accepted;
        }

        foreach (var entry in acceptedBySource)
        {
            foreach (var candidate in entry.Value)
            {
                var evidence = new TrendEvidence(
                    Guid.NewGuid(),
                    candidate.SourceItemId,
                    candidate.RawTopic,
                    candidate.BestPeriod,
                    candidate.BestPeriodProvenance,
                    candidate.RawFinding,
                    candidate.RawEvidenceSummary,
                    candidate.BestConfidence,
                    candidate.BestSourceUrl,
                    candidate.BestQuantitativeEvidence,
                    candidate.BestPublicationName,
                    candidate.BestConceptTags);
                evidence.UpdateTrendFamily(candidate.TrendFamily);

                await _trendEvidenceRepository.AddAsync(evidence, cancellationToken).ConfigureAwait(false);
                persistedCount++;
            }
        }

        await _trendEvidenceRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new TrendAnalysisResult(
            processedCount,
            persistedCount,
            skippedNonTrendResearchCount,
            failedCount,
            duplicateGroupsDetected,
            duplicatesSuppressed);
    }

    private static int CompareCandidateSpecificity(
        TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate left,
        TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate right)
    {
        var findingComparison = CompareByLength(left.RawFinding, right.RawFinding);
        if (findingComparison != 0)
        {
            return findingComparison;
        }

        var summaryComparison = CompareByLength(left.RawEvidenceSummary, right.RawEvidenceSummary);
        if (summaryComparison != 0)
        {
            return summaryComparison;
        }

        var periodComparison = ComparePeriodSpecificity(left.BestPeriod, left.BestPeriodProvenance, right.BestPeriod, right.BestPeriodProvenance);
        if (periodComparison != 0)
        {
            return periodComparison;
        }

        return right.BestConfidence.CompareTo(left.BestConfidence);
    }

    private static int CompareByLength(string left, string right)
    {
        return right.Length.CompareTo(left.Length);
    }

    private static int ComparePeriodSpecificity(
        string leftPeriod,
        TrendEvidencePeriodProvenance leftProvenance,
        string rightPeriod,
        TrendEvidencePeriodProvenance rightProvenance)
    {
        var leftScore = ScorePeriodSpecificity(leftPeriod, leftProvenance);
        var rightScore = ScorePeriodSpecificity(rightPeriod, rightProvenance);
        return rightScore.CompareTo(leftScore);
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

}
