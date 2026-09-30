using AiIntelligence.Application.Intelligence;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public sealed class AnalyzeTrendEvidenceTool : IAnalyzeTrendEvidenceTool
{
    private readonly TrendAnalysisService _trendAnalysisService;

    public AnalyzeTrendEvidenceTool(TrendAnalysisService trendAnalysisService)
    {
        _trendAnalysisService = trendAnalysisService;
    }

    public Task<TrendAnalysisResult> ExecuteAsync(
        AnalyzeTrendEvidenceInput input,
        CancellationToken cancellationToken)
    {
        return _trendAnalysisService.AnalyzeTrendResearchAsync(cancellationToken, input.Limit);
    }
}

public sealed record AnalyzeTrendEvidenceInput(int? Limit);
