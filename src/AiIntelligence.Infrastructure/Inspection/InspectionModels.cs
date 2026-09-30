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
    int RawContentLength,
    int EnrichedContentLength,
    string Preview);

public sealed record TrendInspectionFilter(
    int Limit = 20,
    string? SourceName = null,
    string? Topic = null);

public sealed record TrendInspectionRow(
    Guid Id,
    string SourceName,
    string Topic,
    string Period,
    TrendEvidencePeriodProvenance PeriodProvenance,
    string Finding,
    string EvidenceSummary,
    decimal Confidence,
    Uri SourceUrl);

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
