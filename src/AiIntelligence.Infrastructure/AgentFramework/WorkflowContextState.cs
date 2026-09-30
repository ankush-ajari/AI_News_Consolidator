using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Infrastructure.AgentFramework;

public sealed record WorkflowContextState(
    DateTimeOffset StartedAt,
    AIIntelligenceWorkflowOptions Options,
    IReadOnlyCollection<SourceDefinition> Sources,
    IReadOnlyCollection<WorkflowStageError> StageErrors,
    SourceIngestResult? IngestionResult = null,
    IntelligenceAnalysisResult? CurrentAnalysisResult = null,
    TrendAnalysisResult? TrendAnalysisResult = null,
    IReadOnlyCollection<IntelligenceItem>? IntelligenceItems = null,
    IReadOnlyCollection<TrendCorrelation>? Correlations = null,
    ReportDocument? ReportDocument = null,
    string? Markdown = null,
    string? ReportPath = null)
{
    public static WorkflowContextState Create(
        DateTimeOffset startedAt,
        AIIntelligenceWorkflowOptions options,
        IReadOnlyCollection<SourceDefinition> sources)
    {
        return new WorkflowContextState(
            startedAt,
            options,
            sources,
            Array.Empty<WorkflowStageError>(),
            IntelligenceItems: Array.Empty<IntelligenceItem>(),
            Correlations: Array.Empty<TrendCorrelation>());
    }

    public bool CanContinue => StageErrors.Count == 0;

    public WorkflowContextState AddStageError(string stage, string errorType, string message)
    {
        var errors = StageErrors.Concat(new[] { new WorkflowStageError(stage, errorType, message) }).ToArray();
        return this with { StageErrors = errors };
    }
}
