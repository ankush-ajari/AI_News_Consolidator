using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Persistence;

public sealed record TrendEvidenceCandidate(
    Guid Id,
    Guid SourceItemId,
    string Topic,
    string Period,
    TrendEvidencePeriodProvenance PeriodProvenance,
    string Finding,
    string EvidenceSummary,
    decimal Confidence,
    Uri SourceUrl,
    string PublicationName,
    IReadOnlyCollection<AIConceptTag> ConceptTags,
    TrendFamily TrendFamily);