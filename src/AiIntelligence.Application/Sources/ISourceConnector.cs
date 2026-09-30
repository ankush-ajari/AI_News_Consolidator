using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Sources;

public interface ISourceConnector
{
    SourceType SourceType { get; }

    Task<IReadOnlyCollection<RawSourceItem>> FetchAsync(
        SourceDefinition sourceDefinition,
        CancellationToken cancellationToken);
}
