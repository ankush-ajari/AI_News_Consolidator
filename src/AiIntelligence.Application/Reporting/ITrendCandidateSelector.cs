using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Reporting;

public interface ITrendCandidateSelector
{
    Task<TrendCandidateSelection> SelectCandidatesAsync(
        IntelligenceItem intelligenceItem,
        int maxCandidates,
        CancellationToken cancellationToken);
}

public sealed record TrendCandidateSelection(
    IReadOnlyCollection<TrendEvidence> Candidates,
    IReadOnlyCollection<TrendCandidateDiagnostic> Diagnostics);

public sealed record TrendCandidateDiagnostic(
    Guid TrendEvidenceId,
    string Topic,
    string Period,
    TrendEvidencePeriodProvenance PeriodProvenance,
    string SourceName,
    Uri SourceUrl,
    TrendFamily TrendFamily,
    decimal Confidence,
    bool SameSourceEvidence,
    string MatchReason,
    int Score,
    int Rank,
    bool Selected,
    string? RejectionReason,
    bool Eligible,
    string EligibilityReason,
    int TopicMatchCount,
    int CategoryMatchCount,
    int ProductScore,
    int ConceptMatchCount,
    int SpecificConceptScore,
    string ConceptMatchTags,
    int PeriodScore,
    int ConfidenceScore,
    int FamilyCompatibilityScore,
    int IndependentSourceScore);
