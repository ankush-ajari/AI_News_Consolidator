namespace AiIntelligence.Application.Intelligence;

public sealed record TrendAnalysisResult(
    int ProcessedCount,
    int PersistedCount,
    int SkippedNonTrendResearchCount,
    int FailedCount,
    int DuplicateGroupsDetected,
    int DuplicatesSuppressed);
