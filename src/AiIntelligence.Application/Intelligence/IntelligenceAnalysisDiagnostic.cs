using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Intelligence;

public sealed record IntelligenceAnalysisDiagnostic(
    Guid RawSourceItemId,
    string SourceName,
    string Title,
    DateTimeOffset? PublishedAt,
    int RawContentLength,
    string RawContentPreview,
    IReadOnlyCollection<string> TopicHintsMatched,
    IntelligenceExtractionResult ExtractionResult);
