namespace AiIntelligence.Application.Intelligence;

public sealed record IntelligenceCandidateDiagnostic(
    Guid RawSourceItemId,
    string SourceName,
    string Title,
    int Score,
    IReadOnlyCollection<string> PositiveSignals,
    IReadOnlyCollection<string> NegativeSignals,
    int Rank);
