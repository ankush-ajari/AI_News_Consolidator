using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Sources;

public sealed class SourceConnectorFactory : ISourceConnectorFactory
{
    private readonly IReadOnlyDictionary<SourceType, ISourceConnector> _connectors;

    public SourceConnectorFactory(IEnumerable<ISourceConnector> connectors)
    {
        _connectors = connectors.ToDictionary(connector => connector.SourceType);
    }

    public ISourceConnector GetConnector(SourceType sourceType)
    {
        if (_connectors.TryGetValue(sourceType, out var connector))
        {
            return connector;
        }

        throw new NotSupportedException($"No source connector is registered for source type '{sourceType}'.");
    }
}
