using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Persistence;

// Minimal projection for RawSourceItem to avoid loading large text columns.
public sealed record RawSourceItemSummary(
    Guid Id,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? FetchedAt,
    string CanonicalUrl,
    Guid SourceDefinitionId,
    SourceClass SourceClass);
