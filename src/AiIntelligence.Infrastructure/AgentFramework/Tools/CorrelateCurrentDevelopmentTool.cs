using AiIntelligence.Application.Reporting;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public sealed class CorrelateCurrentDevelopmentTool
{
    private readonly ITrendCandidateSelector _candidateSelector;
    private readonly ITrendCorrelationService _correlationService;

    public CorrelateCurrentDevelopmentTool(
        ITrendCandidateSelector candidateSelector,
        ITrendCorrelationService correlationService)
    {
        _candidateSelector = candidateSelector;
        _correlationService = correlationService;
    }

    public async Task<CorrelateCurrentDevelopmentResult> ExecuteAsync(
        CorrelateCurrentDevelopmentInput input,
        CancellationToken cancellationToken)
    {
        var selection = await _candidateSelector
            .SelectCandidatesAsync(input.IntelligenceItem, input.MaxCandidates, cancellationToken)
            .ConfigureAwait(false);

        var correlation = await _correlationService
            .CorrelateAsync(input.IntelligenceItem, selection.Candidates, cancellationToken)
            .ConfigureAwait(false);

        return new CorrelateCurrentDevelopmentResult(correlation, selection);
    }
}

public sealed record CorrelateCurrentDevelopmentInput(IntelligenceItem IntelligenceItem, int MaxCandidates);

public sealed record CorrelateCurrentDevelopmentResult(
    TrendCorrelation Correlation,
    TrendCandidateSelection CandidateSelection);
