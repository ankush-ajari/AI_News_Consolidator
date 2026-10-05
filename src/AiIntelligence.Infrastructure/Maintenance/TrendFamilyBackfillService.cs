using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Infrastructure.Maintenance;

public sealed class TrendFamilyBackfillService
{
    private readonly AiIntelligenceDbContext _dbContext;
    private readonly ITrendFamilyClassifier _classifier;
    private readonly ILogger<TrendFamilyBackfillService> _logger;

    public TrendFamilyBackfillService(
        AiIntelligenceDbContext dbContext,
        ITrendFamilyClassifier classifier,
        ILogger<TrendFamilyBackfillService> logger)
    {
        _dbContext = dbContext;
        _classifier = classifier;
        _logger = logger;
    }

    public async Task<TrendFamilyBackfillResult> BackfillAsync(bool persist, CancellationToken cancellationToken)
    {
        var total = 0;
        var unknown = 0;
        var requiringUpdate = 0;
        var counts = new Dictionary<TrendFamily, int>();

        var trendEvidence = await _dbContext.TrendEvidence.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var trend in trendEvidence)
        {
            total++;
            var classified = _classifier.Classify(trend.Topic, trend.Finding, trend.EvidenceSummary, trend.ConceptTags);
            if (classified == TrendFamily.Unknown)
            {
                unknown++;
            }

            if (trend.TrendFamily != classified)
            {
                requiringUpdate++;
                if (persist)
                {
                    trend.UpdateTrendFamily(classified);
                }
            }

            counts[classified] = counts.TryGetValue(classified, out var current) ? current + 1 : 1;
        }

        if (persist)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("Trend family backfill dry run complete. No changes were persisted.");
            _dbContext.ChangeTracker.Clear();
        }

        return new TrendFamilyBackfillResult(total, unknown, requiringUpdate, counts);
    }
}

public sealed record TrendFamilyBackfillResult(
    int TotalTrendEvidence,
    int UnknownCount,
    int RecordsRequiringUpdate,
    IReadOnlyDictionary<TrendFamily, int> Counts);
