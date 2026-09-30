namespace AiIntelligence.Infrastructure.AgentFramework;

public sealed record AIIntelligenceWorkflowResult(
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    bool Success,
    string? IngestionSummary,
    string? CurrentAnalysisSummary,
    string? TrendAnalysisSummary,
    int? CorrelationCount,
    int? InsufficientEvidenceCount,
    int? PersonaSectionCount,
    string? ReportPath,
    IReadOnlyCollection<WorkflowStageError> StageErrors,
    IReadOnlyCollection<WorkflowStageResult> StageResults);

public sealed record WorkflowStageError(
    string Stage,
    string ErrorType,
    string Message);

public sealed record WorkflowStageResult(
    string Stage,
    WorkflowStageStatus Status,
    IReadOnlyDictionary<string, string> Metrics);

public enum WorkflowStageStatus
{
    Skipped,
    Completed,
    Failed
}
