using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Infrastructure.Inspection;

public sealed record SourceInspectionRow(
    string SourceName,
    string Vendor,
    SourceType SourceType,
    SourceClass SourceClass,
    bool Enabled,
    int RawRecordCount,
    DateTimeOffset? LatestPublishedAt,
    DateTimeOffset? LatestFetchedAt);

public sealed record RawInspectionFilter(
    int Limit = 20,
    string? SourceName = null,
    SourceClass? SourceClass = null,
    int? MinLength = null);

public sealed record RawInspectionRow(
    Guid Id,
    string SourceName,
    SourceClass SourceClass,
    string Title,
    DateTimeOffset? PublishedAt,
    DateTimeOffset FetchedAt,
    Uri Url,
    string ContentHash,
    int RawContentLength,
    int EnrichedContentLength,
    string Preview);

public sealed record RawDuplicateInspectionGroup(
    Guid SourceDefinitionId,
    string SourceName,
    SourceClass SourceClass,
    string CanonicalUrl,
    IReadOnlyCollection<RawDuplicateInspectionEntry> Entries);

public sealed record RawDuplicateInspectionEntry(
    Guid Id,
    DateTimeOffset? PublishedAt,
    DateTimeOffset FetchedAt,
    int RawContentLength,
    string ContentHash);

public sealed record TrendInspectionFilter(
    int Limit = 20,
    string? SourceName = null,
    string? Topic = null,
    bool IncludeFamily = false);

public sealed record TrendInspectionRow(
    Guid Id,
    string SourceName,
    string Topic,
    string Period,
    TrendEvidencePeriodProvenance PeriodProvenance,
    string Finding,
    string EvidenceSummary,
    decimal Confidence,
    Uri SourceUrl,
    TrendFamily TrendFamily);

public sealed record TrendUnknownInspectionRow(
    Guid Id,
    string Topic,
    string Finding,
    string EvidenceSummary,
    IReadOnlyCollection<AIConceptTag> ConceptTags,
    string SourceName,
    Guid SourceDefinitionId,
    Guid SourceItemId);

public sealed record ConceptInspectionRow(
    string RecordType,
    Guid Id,
    string TitleOrTopic,
    IReadOnlyCollection<AIConceptTag> ConceptTags,
    string SourceName,
    SourceClass SourceClass);

public sealed record TrendDuplicateInspectionEntry(
    string Topic,
    string Finding,
    string EvidenceSummary,
    string Period,
    decimal Confidence,
    TrendFamily TrendFamily);

public sealed record TrendDuplicateInspectionMatch(
    TrendDuplicateInspectionEntry Entry,
    double SimilarityScore,
    double TopicSimilarity,
    double FindingSimilarity,
    double EvidenceSimilarity,
    double ConceptSimilarity,
    string Reason);

public sealed record TrendDuplicateInspectionGroup(
    string SourceName,
    SourceClass SourceClass,
    Guid SourceDefinitionId,
    Guid SourceItemId,
    TrendDuplicateInspectionEntry Canonical,
    IReadOnlyCollection<TrendDuplicateInspectionMatch> Duplicates,
    IReadOnlyCollection<string> ConsistencyWarnings,
    string Reason);

public sealed record SourceGroupRow(string SourceName, SourceClass SourceClass, int Count);

public sealed record ContentQualityStats(
    int MinimumRawContentLength,
    double AverageRawContentLength,
    int MaximumRawContentLength,
    int CountRawContentLessThan300,
    int CountRawContentNullOrEmpty);

public sealed record InspectionStats(
    int SourceDefinitionsCount,
    int RawSourceItemsCount,
    int CurrentOfficialCount,
    int TrendResearchCount,
    int ResearchDiscoveryCount,
    int IntelligenceItemsCount,
    int TrendEvidenceCount,
    IReadOnlyCollection<SourceGroupRow> RawItemsBySource,
    IReadOnlyCollection<SourceGroupRow> TrendEvidenceBySource,
    ContentQualityStats ContentQuality);
