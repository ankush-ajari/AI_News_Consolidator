using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Reporting;

public sealed class MockTrendCorrelationService : ITrendCorrelationService
{
    public Task<TrendCorrelation> CorrelateAsync(IntelligenceItem intelligenceItem, IReadOnlyCollection<TrendEvidence> candidateTrendEvidence, CancellationToken cancellationToken)
    {
        var first = candidateTrendEvidence.FirstOrDefault();
        var relationship = first is null ? CorrelationRelationship.InsufficientEvidence : CorrelationRelationship.Supports;
        var urls = first is null ? new[] { intelligenceItem.SourceUrl } : new[] { intelligenceItem.SourceUrl, first.SourceUrl };
        return Task.FromResult(new TrendCorrelation(
            intelligenceItem.Id,
            intelligenceItem.Summary,
            first?.Finding ?? "Unknown",
            relationship,
            first is null
                ? "Mock correlation: no related trend evidence was selected."
                : "Mock correlation: selected trend evidence appears related by deterministic keyword overlap.",
            first is null
                ? "No trend evidence selected."
                : "Selected trend evidence based on deterministic keyword overlap.",
            first is null ? 0 : 0.5m,
            urls));
    }
}
