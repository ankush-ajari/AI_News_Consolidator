using AiIntelligence.Application.Intelligence;
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
                row.Item.ContentHash.Value,
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
                Source = row.RawItem is null ? null : sources.GetValueOrDefault(row.RawItem.SourceDefinitionId),
                row.Evidence.TrendFamily
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
                row.Evidence.SourceUrl,
                row.TrendFamily))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<TrendUnknownInspectionRow>> GetUnknownTrendsAsync(int limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = NormalizeLimit(limit);
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToDictionaryAsync(source => source.Id, cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        var trendEvidence = await _dbContext.TrendEvidence.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);

        return trendEvidence
            .Where(evidence => evidence.TrendFamily == TrendFamily.Unknown)
            .Select(evidence => new
            {
                Evidence = evidence,
                RawItem = rawItems.GetValueOrDefault(evidence.SourceItemId)
            })
            .Select(row => new
            {
                row.Evidence,
                row.RawItem,
                Source = row.RawItem is null ? null : sources.GetValueOrDefault(row.RawItem.SourceDefinitionId)
            })
            .Where(row => row.RawItem is not null && row.Source is not null)
            .OrderByDescending(row => row.Evidence.Confidence)
            .Take(normalizedLimit)
            .Select(row => new TrendUnknownInspectionRow(
                row.Evidence.Id,
                row.Evidence.Topic,
                row.Evidence.Finding,
                row.Evidence.EvidenceSummary,
                row.Evidence.ConceptTags,
                row.Source!.Name,
                row.Source.Id,
                row.Evidence.SourceItemId))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<ConceptInspectionRow>> GetConceptsAsync(int limit, string? type, CancellationToken cancellationToken)
    {
        var normalizedLimit = NormalizeLimit(limit);
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToDictionaryAsync(source => source.Id, cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);

        var includeIntelligence = string.IsNullOrWhiteSpace(type) || string.Equals(type, "intelligence", StringComparison.OrdinalIgnoreCase);
        var includeTrends = string.IsNullOrWhiteSpace(type) || string.Equals(type, "trends", StringComparison.OrdinalIgnoreCase);

        var rows = new List<ConceptInspectionRow>();

        if (includeIntelligence)
        {
            var intelligenceItems = await _dbContext.IntelligenceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in intelligenceItems)
            {
                if (!rawItems.TryGetValue(item.SourceItemId, out var rawItem)
                    || !sources.TryGetValue(rawItem.SourceDefinitionId, out var source))
                {
                    continue;
                }

                rows.Add(new ConceptInspectionRow(
                    "IntelligenceItem",
                    item.Id,
                    item.Topic,
                    item.ConceptTags,
                    source.Name,
                    source.SourceClass));
            }
        }

        if (includeTrends)
        {
            var trendEvidence = await _dbContext.TrendEvidence.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (var evidence in trendEvidence)
            {
                if (!rawItems.TryGetValue(evidence.SourceItemId, out var rawItem)
                    || !sources.TryGetValue(rawItem.SourceDefinitionId, out var source))
                {
                    continue;
                }

                rows.Add(new ConceptInspectionRow(
                    "TrendEvidence",
                    evidence.Id,
                    evidence.Topic,
                    evidence.ConceptTags,
                    source.Name,
                    source.SourceClass));
            }
        }

        return rows
            .OrderBy(row => row.RecordType)
            .ThenBy(row => row.TitleOrTopic)
            .Take(normalizedLimit)
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

    public async Task<IReadOnlyCollection<TrendDuplicateInspectionGroup>> GetTrendDuplicateGroupsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = NormalizeLimit(limit);
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToDictionaryAsync(source => source.Id, cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        var trendEvidence = await _dbContext.TrendEvidence.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var detector = new TrendEvidenceDuplicateDetector();

        var groupedBySource = trendEvidence
            .Select(evidence => new
            {
                Evidence = evidence,
                RawItem = rawItems.GetValueOrDefault(evidence.SourceItemId)
            })
            .Where(row => row.RawItem is not null)
            .GroupBy(row => row.RawItem!.SourceDefinitionId);

        var rows = new List<TrendDuplicateInspectionGroup>();

        foreach (var sourceGroup in groupedBySource)
        {
            if (!sources.TryGetValue(sourceGroup.Key, out var source))
            {
                continue;
            }

            var accepted = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
            var duplicates = new Dictionary<Guid, List<TrendEvidenceDuplicateMatch>>();

            foreach (var row in sourceGroup)
            {
                var candidate = detector.CreateCandidate(row.Evidence, sourceGroup.Key, row.Evidence.TrendFamily);
                if (detector.TryMergeCandidate(candidate, accepted, out var match))
                {
                    var canonical = accepted[match!.MatchedIndex];
                    if (!duplicates.TryGetValue(canonical.CanonicalId, out var list))
                    {
                        list = new List<TrendEvidenceDuplicateMatch>();
                        duplicates[canonical.CanonicalId] = list;
                    }

                    list.Add(new TrendEvidenceDuplicateMatch(match.Duplicate, match.Similarity));
                }
                else
                {
                    accepted.Add(candidate);
                }
            }

            foreach (var entry in duplicates)
            {
                var canonical = accepted.FirstOrDefault(candidate => candidate.CanonicalId == entry.Key);
                if (canonical is null)
                {
                    continue;
                }

                var canonicalEntry = new TrendDuplicateInspectionEntry(
                    canonical.RawTopic,
                    canonical.RawFinding,
                    canonical.RawEvidenceSummary,
                    canonical.BestPeriod,
                    canonical.BestConfidence,
                    canonical.TrendFamily);

                var warnings = new List<string>();
                var consistency = detector.EvaluateConsistency(canonical);
                if (!consistency.IsConsistent)
                {
                    warnings.Add(consistency.Reason);
                }

                var matches = entry.Value
                    .Select(duplicate =>
                    {
                        var similarity = detector.EvaluateSimilarity(duplicate.Duplicate, canonical);
                        return new TrendDuplicateInspectionMatch(
                            new TrendDuplicateInspectionEntry(
                                duplicate.Duplicate.RawTopic,
                                duplicate.Duplicate.RawFinding,
                                duplicate.Duplicate.RawEvidenceSummary,
                                duplicate.Duplicate.BestPeriod,
                                duplicate.Duplicate.BestConfidence,
                                duplicate.Duplicate.TrendFamily),
                            similarity.WeightedScore,
                            similarity.TopicSimilarity,
                            similarity.FindingSimilarity,
                            similarity.EvidenceSimilarity,
                            similarity.ConceptSimilarity,
                            similarity.Reason);
                    })
                    .ToArray();

                rows.Add(new TrendDuplicateInspectionGroup(
                    source.Name,
                    source.SourceClass,
                    sourceGroup.Key,
                    canonical.SourceItemId,
                    canonicalEntry,
                    matches,
                    warnings,
                    $"Consolidated by TrendFamily {canonical.TrendFamily} and similarity checks."));
            }
        }

        return rows
            .OrderBy(row => row.SourceName)
            .ThenBy(row => row.SourceItemId)
            .Take(normalizedLimit)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<RawDuplicateInspectionGroup>> GetRawDuplicateGroupsAsync(
        CancellationToken cancellationToken)
    {
        var sources = await _dbContext.SourceDefinitions.AsNoTracking().ToDictionaryAsync(source => source.Id, cancellationToken).ConfigureAwait(false);
        var rawItems = await _dbContext.RawSourceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);

        var groups = rawItems
            .GroupBy(item => new { item.SourceDefinitionId, item.CanonicalUrl })
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                sources.TryGetValue(group.Key.SourceDefinitionId, out var source);
                return new RawDuplicateInspectionGroup(
                    group.Key.SourceDefinitionId,
                    source?.Name ?? "Unknown",
                    source?.SourceClass ?? SourceClass.CurrentOfficial,
                    group.Key.CanonicalUrl,
                    group.OrderByDescending(item => item.FetchedAt)
                        .Select(item => new RawDuplicateInspectionEntry(
                            item.Id,
                            item.PublishedAt,
                            item.FetchedAt,
                            item.RawContent.Length,
                            item.ContentHash.Value))
                        .ToArray());
            })
            .OrderBy(group => group.SourceName)
            .ThenBy(group => group.CanonicalUrl)
            .ToArray();

        return groups;
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
