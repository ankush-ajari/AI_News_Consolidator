using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Reporting;

public interface IPersonaReportGenerator
{
    Task<ReportDocument> GenerateAsync(
        IReadOnlyCollection<IntelligenceItem> intelligenceItems,
        IReadOnlyCollection<TrendCorrelation> correlations,
        IReadOnlyCollection<TrendEvidence> trendEvidence,
        CancellationToken cancellationToken);
}
