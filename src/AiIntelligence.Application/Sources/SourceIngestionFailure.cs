using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Sources;

public sealed record SourceIngestionFailure(
    SourceDefinition Source,
    string ErrorMessage,
    Exception Exception);
