namespace AiIntelligence.Application.Reporting;

public sealed record ReportGenerationResult(
    ReportDocument Document,
    int IntelligenceItemsProcessed,
    int TrendCandidatesConsidered,
    int CorrelationsCreated,
    int InsufficientEvidenceCorrelations,
    int PersonaSectionsCreated,
    int LlmCallCount);
