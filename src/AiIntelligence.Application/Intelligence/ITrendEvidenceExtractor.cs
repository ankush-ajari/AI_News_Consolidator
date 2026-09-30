using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Intelligence;

public interface ITrendEvidenceExtractor
{
    Task<TrendEvidenceExtractionBatch> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken);
}
