using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Intelligence;

public interface IIntelligenceExtractor
{
    Task<IntelligenceExtractionResult> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken);
}
