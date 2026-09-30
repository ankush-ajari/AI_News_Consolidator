using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Reporting;

public interface ITrendCorrelationService
{
    Task<TrendCorrelation> CorrelateAsync(
        IntelligenceItem intelligenceItem,
        IReadOnlyCollection<TrendEvidence> candidateTrendEvidence,
        CancellationToken cancellationToken);
}
