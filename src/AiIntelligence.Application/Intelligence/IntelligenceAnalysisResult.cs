namespace AiIntelligence.Application.Intelligence;

public sealed record IntelligenceAnalysisResult(
    int ProcessedCount,
    int PersistedCount,
    int IrrelevantCount,
    int FailedCount,
    int SkippedNonCurrentOfficialCount,
    string RelevanceCriteriaPrompt,
    IReadOnlyCollection<IntelligenceCandidateDiagnostic> CandidateDiagnostics,
    IReadOnlyCollection<IntelligenceCandidateDiagnostic> SelectedCandidates,
    IReadOnlyCollection<IntelligenceAnalysisDiagnostic> Diagnostics);
