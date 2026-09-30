using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Sources;

public interface ISourceConnectorFactory
{
    ISourceConnector GetConnector(SourceType sourceType);
}
