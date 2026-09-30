namespace AiIntelligence.Infrastructure.AgentFramework;

public sealed class AIIntelligenceWorkflowOptions
{
    public int? CurrentIntelligenceLimit { get; init; }

    public int? TrendAnalysisLimit { get; init; }

    public bool Verbose { get; init; }

    public bool MockLlm { get; init; }

    public bool RunIngestion { get; init; } = true;

    public bool RunCurrentAnalysis { get; init; } = true;

    public bool RunTrendAnalysis { get; init; } = true;

    public bool RunReportGeneration { get; init; } = true;
}
