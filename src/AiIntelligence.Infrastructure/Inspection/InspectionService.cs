using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AiIntelligence.Infrastructure.Inspection;

public sealed class InspectionService
{
    private readonly Persistence.AiIntelligenceDbContext _dbContext;

    public InspectionService(Persistence.AiIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<SourceInspectionRow>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return await GetSourcesAsync(sources, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<SourceInspectionRow>> GetSourcesAsync(
        IReadOnlyCollection<SourceDefinition> configuredSources,
        CancellationToken cancellationToken)
    {
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return configuredSources
            .OrderBy(source => source.Name)
            .Select(source =>
            {
                var sourceItems = rawItems.Where(item => item.SourceDefinitionId == source.Id).ToArray();
                return new SourceInspectionRow(
                    source.Name,
                    source.Vendor,
                    source.SourceType,
                    source.SourceClass,
                    source.IsEnabled,
                    sourceItems.Length,
                    sourceItems.Select(item => item.PublishedAt).Where(value => value.HasValue).DefaultIfEmpty().Max(),
                    sourceItems.Length == 0 ? null : sourceItems.Max(item => item.FetchedAt));
            })
            .ToArray();
    }

    public async Task<IReadOnlyCollection<RawInspectionRow>> GetRawAsync(RawInspectionFilter filter, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(filter.Limit);
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToDictionaryAsync(source => source.Id, cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return rawItems
            .Select(item => new { Item = item, Source = sources.GetValueOrDefault(item.SourceDefinitionId) })
            .Where(row => row.Source is not null)
            .Where(row => filter.SourceName is null || row.Source!.Name.Equals(filter.SourceName, StringComparison.OrdinalIgnoreCase))
            .Where(row => filter.SourceClass is null || row.Item.SourceClass == filter.SourceClass)
            .Where(row => filter.MinLength is null || row.Item.RawContent.Length >= filter.MinLength.Value)
            .OrderByDescending(row => row.Item.FetchedAt)
            .Take(limit)
            .Select(row => new RawInspectionRow(
                row.Item.Id,
                row.Source!.Name,
                row.Item.SourceClass,
                row.Item.Title,
                row.Item.PublishedAt,
                row.Item.FetchedAt,
                row.Item.Url,
                row.Item.RawContent.Length,
                row.Item.EnrichedContent.Length,
                CreatePreview(row.Item.RawContent)))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<TrendInspectionRow>> GetTrendsAsync(TrendInspectionFilter filter, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(filter.Limit);
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToDictionaryAsync(source => source.Id, cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        var trendEvidence = await _dbContext.TrendEvidence.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return trendEvidence
            .Select(evidence => new
            {
                Evidence = evidence,
                RawItem = rawItems.GetValueOrDefault(evidence.SourceItemId),
            })
            .Select(row => new
            {
                row.Evidence,
                row.RawItem,
                Source = row.RawItem is null ? null : sources.GetValueOrDefault(row.RawItem.SourceDefinitionId)
            })
            .Where(row => row.Source is not null)
            .Where(row => filter.SourceName is null || row.Source!.Name.Equals(filter.SourceName, StringComparison.OrdinalIgnoreCase))
            .Where(row => filter.Topic is null || row.Evidence.Topic.Contains(filter.Topic, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.Evidence.Confidence)
            .Take(limit)
            .Select(row => new TrendInspectionRow(
                row.Evidence.Id,
                row.Source!.Name,
                row.Evidence.Topic,
                row.Evidence.Period,
                row.Evidence.PeriodProvenance,
                row.Evidence.Finding,
                row.Evidence.EvidenceSummary,
                row.Evidence.Confidence,
                row.Evidence.SourceUrl))
            .ToArray();
    }

    public async Task<InspectionStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var intelligenceCount = await _dbContext.IntelligenceItems.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
        var trendEvidence = await _dbContext.TrendEvidence.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var sourceLookup = sources.ToDictionary(source => source.Id);
        var rawLookup = rawItems.ToDictionary(item => item.Id);

        var rawLengths = rawItems.Select(item => item.RawContent?.Length ?? 0).ToArray();
        var quality = rawLengths.Length == 0
            ? new ContentQualityStats(0, 0, 0, 0, 0)
            : new ContentQualityStats(
                rawLengths.Min(),
                rawLengths.Average(),
                rawLengths.Max(),
                rawLengths.Count(length => length < 300),
                rawItems.Count(item => string.IsNullOrEmpty(item.RawContent)));

        return new InspectionStats(
            sources.Length,
            rawItems.Length,
            rawItems.Count(item => item.SourceClass == SourceClass.CurrentOfficial),
            rawItems.Count(item => item.SourceClass == SourceClass.TrendResearch),
            rawItems.Count(item => item.SourceClass == SourceClass.ResearchDiscovery),
            intelligenceCount,
            trendEvidence.Length,
            rawItems
                .Where(item => sourceLookup.ContainsKey(item.SourceDefinitionId))
                .GroupBy(item => new { Source = sourceLookup[item.SourceDefinitionId], item.SourceClass })
                .Select(group => new SourceGroupRow(group.Key.Source.Name, group.Key.SourceClass, group.Count()))
                .OrderBy(row => row.SourceName)
                .ToArray(),
            trendEvidence
                .Select(evidence => rawLookup.TryGetValue(evidence.SourceItemId, out var rawItem) && sourceLookup.TryGetValue(rawItem.SourceDefinitionId, out var source)
                    ? source
                    : null)
                .Where(source => source is not null)
                .GroupBy(source => source!)
                .Select(group => new SourceGroupRow(group.Key.Name, group.Key.SourceClass, group.Count()))
                .OrderBy(row => row.SourceName)
                .ToArray(),
            quality);
    }

    private static int NormalizeLimit(int limit) => limit <= 0 ? 20 : Math.Min(limit, 500);

    private static string CreatePreview(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }
}
