using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public sealed class IngestSourcesTool
{
    private readonly SourceIngestionService _sourceIngestionService;

    public IngestSourcesTool(SourceIngestionService sourceIngestionService)
    {
        _sourceIngestionService = sourceIngestionService;
    }

    public Task<SourceIngestResult> ExecuteAsync(
        IngestSourcesInput input,
        CancellationToken cancellationToken)
    {
        return _sourceIngestionService.IngestAsync(input.SourceDefinitions, cancellationToken);
    }
}

public sealed record IngestSourcesInput(IReadOnlyCollection<SourceDefinition> SourceDefinitions);
