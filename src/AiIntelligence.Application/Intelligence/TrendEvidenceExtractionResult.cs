using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Intelligence;

public sealed record TrendEvidenceExtractionResult(
    string Topic,
    string Period,
    TrendEvidencePeriodProvenance PeriodProvenance,
    string Finding,
    string QuantitativeEvidence,
    string EvidenceSummary,
    decimal Confidence,
    Uri SourceUrl,
    string PublicationName);
