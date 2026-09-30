using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Sources;

public sealed record SourceIngestionResult(
    IReadOnlyCollection<RawSourceItem> Items,
    IReadOnlyCollection<SourceIngestionFailure> Failures);
