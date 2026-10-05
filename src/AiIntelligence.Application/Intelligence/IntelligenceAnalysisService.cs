using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Intelligence;

public sealed class IntelligenceAnalysisService
{
    private readonly IRawSourceRepository _rawSourceRepository;
    private readonly ISourceDefinitionRepository _sourceDefinitionRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly IIntelligenceExtractor _extractor;
    private readonly IAIConceptClassifier _conceptClassifier;
    private readonly ILogger<IntelligenceAnalysisService> _logger;

    public IntelligenceAnalysisService(
        IRawSourceRepository rawSourceRepository,
        ISourceDefinitionRepository sourceDefinitionRepository,
        IIntelligenceRepository intelligenceRepository,
        IIntelligenceExtractor extractor,
        ILogger<IntelligenceAnalysisService> logger)
        : this(rawSourceRepository, sourceDefinitionRepository, intelligenceRepository, extractor, new DeterministicAIConceptClassifier(), logger)
    {
    }

    public IntelligenceAnalysisService(
        IRawSourceRepository rawSourceRepository,
        ISourceDefinitionRepository sourceDefinitionRepository,
        IIntelligenceRepository intelligenceRepository,
        IIntelligenceExtractor extractor,
        IAIConceptClassifier conceptClassifier,
        ILogger<IntelligenceAnalysisService> logger)
    {
        _rawSourceRepository = rawSourceRepository;
        _sourceDefinitionRepository = sourceDefinitionRepository;
        _intelligenceRepository = intelligenceRepository;
        _extractor = extractor;
        _conceptClassifier = conceptClassifier;
        _logger = logger;
    }

    private static readonly string[] TopicHints =
    {
        "ai", "artificial intelligence", "machine learning", "ml", "llm", "gpt", "genai", "generative", "model",
        "agent", "copilot", "azure ai", "openai", "deep learning", "foundation model"
    };

    public async Task<IntelligenceAnalysisResult> AnalyzeUnprocessedAsync(
        CancellationToken cancellationToken,
        int? limit = null,
        bool includeDiagnostics = false)
    {
        var rawItems = await _rawSourceRepository.ListUnprocessedAsync(cancellationToken).ConfigureAwait(false);
        var remainingLimit = limit is > 0 ? limit.Value : (int?)null;
        var processedCount = 0;
        var persistedCount = 0;
        var irrelevantCount = 0;
        var failedCount = 0;
        var skippedNonCurrentOfficialCount = 0;
        var diagnostics = includeDiagnostics
            ? new List<IntelligenceAnalysisDiagnostic>()
            : new List<IntelligenceAnalysisDiagnostic>(0);
        var candidateBuffer = new List<(RawSourceItem RawItem, SourceDefinition? SourceDefinition, AiRelevanceScore Score)>();

        foreach (var rawItem in rawItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceDefinition = await _sourceDefinitionRepository.GetByIdAsync(rawItem.SourceDefinitionId, cancellationToken).ConfigureAwait(false);
            var sourceClass = sourceDefinition?.SourceClass ?? rawItem.SourceClass;
            if (sourceClass != Domain.Enums.SourceClass.CurrentOfficial)
            {
                skippedNonCurrentOfficialCount++;
                continue;
            }

            if (await _intelligenceRepository.ExistsForSourceItemAsync(rawItem.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var score = AiRelevanceScorer.Score(rawItem, sourceDefinition);
            candidateBuffer.Add((rawItem, sourceDefinition, score));
        }

        var orderedCandidates = candidateBuffer
            .OrderByDescending(candidate => candidate.Score.Score)
            .ThenByDescending(candidate => candidate.RawItem.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(candidate => candidate.RawItem.FetchedAt)
            .Select((candidate, index) => new
            {
                candidate.RawItem,
                candidate.SourceDefinition,
                candidate.Score,
                Rank = index + 1
            })
            .ToArray();

        var candidateDiagnostics = orderedCandidates
            .Select(candidate => new IntelligenceCandidateDiagnostic(
                candidate.RawItem.Id,
                candidate.SourceDefinition?.Name ?? "Unknown source",
                candidate.RawItem.Title,
                candidate.Score.Score,
                candidate.Score.PositiveSignals,
                candidate.Score.NegativeSignals,
                candidate.Rank))
            .ToArray();

        var selectedCandidates = orderedCandidates
            .Take(remainingLimit ?? int.MaxValue)
            .Select(candidate => new IntelligenceCandidateDiagnostic(
                candidate.RawItem.Id,
                candidate.SourceDefinition?.Name ?? "Unknown source",
                candidate.RawItem.Title,
                candidate.Score.Score,
                candidate.Score.PositiveSignals,
                candidate.Score.NegativeSignals,
                candidate.Rank))
            .ToArray();

        foreach (var candidate in orderedCandidates.Take(remainingLimit ?? int.MaxValue))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                processedCount++;

                var extraction = await _extractor.ExtractAsync(candidate.RawItem, cancellationToken).ConfigureAwait(false);

                if (includeDiagnostics)
                {
                    diagnostics.Add(new IntelligenceAnalysisDiagnostic(
                        candidate.RawItem.Id,
                        candidate.SourceDefinition?.Name ?? "Unknown source",
                        candidate.RawItem.Title,
                        candidate.RawItem.PublishedAt,
                        candidate.RawItem.RawContent.Length,
                        CreatePreview(candidate.RawItem.RawContent, 450),
                        GetTopicHints(candidate.RawItem),
                        extraction));
                }

                if (!extraction.IsRelevantToAI)
                {
                    irrelevantCount++;
                    continue;
                }

                if (candidate.SourceDefinition is null)
                {
                    _logger.LogWarning("Missing source definition for raw source item {SourceItemId}; using source class {SourceClass}.", candidate.RawItem.Id, candidate.RawItem.SourceClass);
                }

                var conceptTags = _conceptClassifier.Classify(
                    candidate.RawItem.Title,
                    extraction.Topic,
                    extraction.Category,
                    extraction.ProductOrFramework,
                    extraction.Summary);

                var intelligenceItem = new IntelligenceItem(
                    Guid.NewGuid(),
                    candidate.RawItem.Id,
                    extraction.Vendor,
                    extraction.Topic,
                    extraction.Category,
                    extraction.ProductOrFramework,
                    extraction.Summary,
                    extraction.Capabilities,
                    extraction.Limitations,
                    extraction.ReleaseStage,
                    candidate.SourceDefinition?.SourceClass ?? candidate.RawItem.SourceClass,
                    candidate.RawItem.PublishedAt,
                    candidate.RawItem.Url,
                    conceptTags);

                await _intelligenceRepository.AddAsync(intelligenceItem, cancellationToken).ConfigureAwait(false);
                persistedCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedCount++;
                _logger.LogError(exception, "Failed to analyze raw source item {SourceItemId}.", candidate.RawItem.Id);
            }
        }

        await _intelligenceRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new IntelligenceAnalysisResult(
            processedCount,
            persistedCount,
            irrelevantCount,
            failedCount,
            skippedNonCurrentOfficialCount,
            LlmIntelligenceExtractor.BuildSystemPrompt(),
            candidateDiagnostics,
            selectedCandidates,
            diagnostics);
    }

    private static IReadOnlyCollection<string> GetTopicHints(RawSourceItem rawItem)
    {
        var content = string.Concat(rawItem.Title, " ", rawItem.RawContent ?? string.Empty);
        return TopicHints
            .Where(hint => content.Contains(hint, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreatePreview(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
