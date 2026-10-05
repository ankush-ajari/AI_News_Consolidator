using AiIntelligence.Application.Intelligence;
using AiIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Infrastructure.Maintenance;

public sealed class ConceptBackfillService
{
    private readonly AiIntelligenceDbContext _dbContext;
    private readonly IAIConceptClassifier _classifier;
    private readonly ILogger<ConceptBackfillService> _logger;

    public ConceptBackfillService(
        AiIntelligenceDbContext dbContext,
        IAIConceptClassifier classifier,
        ILogger<ConceptBackfillService> logger)
    {
        _dbContext = dbContext;
        _classifier = classifier;
        _logger = logger;
    }

    public async Task<ConceptBackfillResult> BackfillAsync(bool persist, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var itemsNeedingBackfill = 0;
        var trendsNeedingBackfill = 0;

        var intelligenceItems = await _dbContext.IntelligenceItems.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in intelligenceItems)
        {
            if (item.ConceptTags.Count == 0)
            {
                itemsNeedingBackfill++;
            }

            var tags = _classifier.Classify(
                string.Empty,
                item.Topic,
                item.Category,
                item.ProductOrFramework,
                item.Summary);
            item.UpdateConceptTags(tags);
            AddCounts(counts, tags);
        }

        var trendEvidence = await _dbContext.TrendEvidence.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var trend in trendEvidence)
        {
            if (trend.ConceptTags.Count == 0)
            {
                trendsNeedingBackfill++;
            }

            var tags = _classifier.Classify(
                string.Empty,
                trend.Topic,
                string.Empty,
                string.Empty,
                trend.Finding);
            trend.UpdateConceptTags(tags);
            AddCounts(counts, tags);
        }

        if (persist)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("Concept backfill dry run complete. No changes were persisted.");
            _dbContext.ChangeTracker.Clear();
        }

        return new ConceptBackfillResult(counts, itemsNeedingBackfill, trendsNeedingBackfill);
    }

    private static void AddCounts(Dictionary<string, int> counts, IEnumerable<AiIntelligence.Domain.Enums.AIConceptTag> tags)
    {
        foreach (var tag in tags)
        {
            var key = tag.ToString();
            counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
        }
    }
}

public sealed record ConceptBackfillResult(
    IReadOnlyDictionary<string, int> Counts,
    int IntelligenceItemsNeedingBackfill,
    int TrendEvidenceNeedingBackfill);
