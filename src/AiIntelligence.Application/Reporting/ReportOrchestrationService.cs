using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class ReportOrchestrationService
{
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly ITrendCandidateSelector _candidateSelector;
    private readonly ITrendCorrelationService _correlationService;
    private readonly IPersonaReportGenerator _reportGenerator;
    private readonly MarkdownReportRenderer _renderer;
    private readonly ILogger<ReportOrchestrationService> _logger;

    public ReportOrchestrationService(
        IIntelligenceRepository intelligenceRepository,
        ITrendCandidateSelector candidateSelector,
        ITrendCorrelationService correlationService,
        IPersonaReportGenerator reportGenerator,
        MarkdownReportRenderer renderer,
        ILogger<ReportOrchestrationService> logger)
    {
        _intelligenceRepository = intelligenceRepository;
        _candidateSelector = candidateSelector;
        _correlationService = correlationService;
        _reportGenerator = reportGenerator;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<(ReportGenerationResult Result, string Markdown)> GenerateAsync(int? limit, bool verbose, CancellationToken cancellationToken)
    {
        var intelligenceItems = (await _intelligenceRepository.ListAsync(cancellationToken).ConfigureAwait(false))
            .Take(limit is > 0 ? limit.Value : int.MaxValue)
            .ToArray();
        var correlations = new List<TrendCorrelation>();
        var allCandidates = new List<TrendEvidence>();
        var llmCallCount = 0;

        foreach (var item in intelligenceItems)
        {
            var selection = await _candidateSelector.SelectCandidatesAsync(item, 5, cancellationToken).ConfigureAwait(false);
            var candidates = selection.Candidates;
            allCandidates.AddRange(candidates);
            if (verbose)
            {
                _logger.LogInformation("CURRENT INTELLIGENCE ITEM");
                _logger.LogInformation("- Id: {Id}", item.Id);
                _logger.LogInformation("- Topic: {Topic}", item.Topic);
                _logger.LogInformation("- Category: {Category}", item.Category);
                _logger.LogInformation("- Product/framework: {ProductOrFramework}", item.ProductOrFramework);
                _logger.LogInformation("- Source URL: {SourceUrl}", item.SourceUrl);
                _logger.LogInformation("- Summary: {Summary}", item.Summary);
                _logger.LogInformation("CANDIDATE TREND EVIDENCE");
                foreach (var candidate in selection.Diagnostics)
                {
                    _logger.LogInformation(
                        "- Id: {Id}; Topic: {Topic}; Period: {Period}; Source: {Source}; Source URL: {Url}; SameSourceEvidence: {SameSource}; Match reasons: {MatchReason}; Candidate score/rank: {Score}/{Rank}",
                        candidate.TrendEvidenceId,
                        candidate.Topic,
                        candidate.Period,
                        candidate.SourceName,
                        candidate.SourceUrl,
                        candidate.SameSourceEvidence ? "Yes" : "No",
                        candidate.MatchReason,
                        candidate.Score,
                        candidate.Rank);
                }
            }

            var correlation = await _correlationService.CorrelateAsync(item, candidates, cancellationToken).ConfigureAwait(false);
            correlations.Add(correlation);
            if (candidates.Count > 0)
            {
                llmCallCount++;
            }

            if (verbose)
            {
                _logger.LogInformation("SELECTED TREND EVIDENCE");
                foreach (var candidate in selection.Diagnostics)
                {
                    if (candidate.Selected)
                    {
                        _logger.LogInformation(
                            "- Selected Id: {Id}; Reason: {MatchReason}",
                            candidate.TrendEvidenceId,
                            candidate.MatchReason);
                    }
                    else if (!string.IsNullOrWhiteSpace(candidate.RejectionReason))
                    {
                        _logger.LogInformation(
                            "- Rejected Id: {Id}; Reason: {Reason}",
                            candidate.TrendEvidenceId,
                            candidate.RejectionReason);
                    }
                }

                _logger.LogInformation("CORRELATION RESULT");
                _logger.LogInformation("- Relationship: {Relationship}", correlation.Relationship);
                _logger.LogInformation("- Confidence: {Confidence}", correlation.Confidence);
                _logger.LogInformation("- EvidenceBasis: {EvidenceBasis}", correlation.EvidenceBasis);
                _logger.LogInformation("- SupportingSourceUrls: {Urls}", string.Join(", ", correlation.SupportingSourceUrls));
            }
        }

        var distinctCandidates = allCandidates.GroupBy(candidate => candidate.Id).Select(group => group.First()).ToArray();
        var document = await _reportGenerator.GenerateAsync(intelligenceItems, correlations, distinctCandidates, cancellationToken).ConfigureAwait(false);
        llmCallCount += 5;
        var markdown = _renderer.Render(document);
        var result = new ReportGenerationResult(
            document,
            intelligenceItems.Length,
            allCandidates.Count,
            correlations.Count,
            correlations.Count(correlation => correlation.Relationship == CorrelationRelationship.InsufficientEvidence),
            document.PersonaSections.Count,
            llmCallCount);
        return (result, markdown);
    }
}
