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
    IReadOnlyCollection<TrendEvidence>? SelectedTrendEvidence = null,
    ReportDocument? ReportDocument = null,
    string? Markdown = null,
    string? ReportPath = null,
    string? DocxReportPath = null)
{
    // Memory diagnostics removed from persisted workflow state to avoid
    // retaining large payloads in the workflow engine's historical state.
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
            Correlations: Array.Empty<TrendCorrelation>(),
            SelectedTrendEvidence: Array.Empty<TrendEvidence>());
    }

    // AddStageMemoryMetrics intentionally removed. Memory diagnostics are now
    // emitted via logging only to avoid embedding diagnostic samples in the
    // immutable workflow state which is retained by the execution engine.

    public bool CanContinue => StageErrors.Count == 0;

    public WorkflowContextState AddStageError(string stage, string errorType, string message)
    {
        var errors = StageErrors.Concat(new[] { new WorkflowStageError(stage, errorType, message) }).ToArray();
        return this with { StageErrors = errors };
    }
}
