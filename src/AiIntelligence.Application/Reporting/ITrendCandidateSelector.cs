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
    string SourceName,
    Uri SourceUrl,
    bool SameSourceEvidence,
    string MatchReason,
    int Score,
    int Rank,
    bool Selected,
    string? RejectionReason);
