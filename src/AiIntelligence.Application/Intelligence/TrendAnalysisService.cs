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
    private readonly ILogger<TrendAnalysisService> _logger;

    public TrendAnalysisService(
        IRawSourceRepository rawSourceRepository,
        ISourceDefinitionRepository sourceDefinitionRepository,
        ITrendEvidenceRepository trendEvidenceRepository,
        ITrendEvidenceExtractor extractor,
        ILogger<TrendAnalysisService> logger)
    {
        _rawSourceRepository = rawSourceRepository;
        _sourceDefinitionRepository = sourceDefinitionRepository;
        _trendEvidenceRepository = trendEvidenceRepository;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<TrendAnalysisResult> AnalyzeTrendResearchAsync(CancellationToken cancellationToken, int? limit = null)
    {
        var rawItems = await _rawSourceRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        var remainingLimit = limit is > 0 ? limit.Value : (int?)null;
        var processedCount = 0;
        var persistedCount = 0;
        var skippedNonTrendResearchCount = 0;
        var failedCount = 0;

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

                var extractionBatch = await _extractor.ExtractAsync(rawItem, cancellationToken).ConfigureAwait(false);
                foreach (var extracted in extractionBatch.Items)
                {
                    var periodResolution = TrendEvidencePeriodResolver.Resolve(extracted, rawItem, sourceDefinition);
                    var evidence = new TrendEvidence(
                        Guid.NewGuid(),
                        rawItem.Id,
                        extracted.Topic,
                        periodResolution.Period,
                        periodResolution.Provenance,
                        extracted.Finding,
                        extracted.EvidenceSummary,
                        extracted.Confidence,
                        extracted.SourceUrl,
                        extracted.QuantitativeEvidence,
                        extracted.PublicationName);

                    await _trendEvidenceRepository.AddAsync(evidence, cancellationToken).ConfigureAwait(false);
                    persistedCount++;
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

        await _trendEvidenceRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new TrendAnalysisResult(processedCount, persistedCount, skippedNonTrendResearchCount, failedCount);
    }
}
