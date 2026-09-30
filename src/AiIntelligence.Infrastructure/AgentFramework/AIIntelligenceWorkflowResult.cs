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
    IReadOnlyCollection<WorkflowStageError> StageErrors);

public sealed record WorkflowStageError(
    string Stage,
    string ErrorType,
    string Message);
