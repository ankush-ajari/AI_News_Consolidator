using AiIntelligence.Application.Intelligence;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public sealed class AnalyzeCurrentIntelligenceTool
{
    private readonly IntelligenceAnalysisService _analysisService;

    public AnalyzeCurrentIntelligenceTool(IntelligenceAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    public Task<IntelligenceAnalysisResult> ExecuteAsync(
        AnalyzeCurrentIntelligenceInput input,
        CancellationToken cancellationToken)
    {
        return _analysisService.AnalyzeUnprocessedAsync(
            cancellationToken,
            input.Limit,
            includeDiagnostics: input.IncludeDiagnostics);
    }
}

public sealed record AnalyzeCurrentIntelligenceInput(int? Limit, bool IncludeDiagnostics);
