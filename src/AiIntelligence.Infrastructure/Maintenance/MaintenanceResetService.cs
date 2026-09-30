using AiIntelligence.Domain.Enums;
using AiIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Infrastructure.Maintenance;

public sealed record ResetTrendsResult(int DeletedTrendEvidenceCount);

public sealed record ResetIntelligenceResult(int DeletedIntelligenceItemsCount);

public sealed record ResetAnalysisResult(int DeletedIntelligenceItemsCount, int DeletedTrendEvidenceCount);

public sealed record ResetInvalidIntelligenceResult(
    int InvalidIntelligenceCount,
    int DeletedCount,
    int RemainingValidCount,
    IReadOnlyCollection<InvalidIntelligenceDetail> DeletedItems);

public sealed record InvalidIntelligenceDetail(
    Guid IntelligenceItemId,
    string SourceName,
    SourceClass SourceClass,
    Uri SourceUrl);

public sealed class MaintenanceResetService
{
    private readonly AiIntelligenceDbContext _dbContext;
    private readonly ILogger<MaintenanceResetService> _logger;

    public MaintenanceResetService(AiIntelligenceDbContext dbContext, ILogger<MaintenanceResetService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ResetTrendsResult> ResetTrendsAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            _logger.LogWarning("Reset trends requested without confirmation. No rows deleted.");
            return new ResetTrendsResult(0);
        }

        var deleted = await _dbContext.TrendEvidence.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return new ResetTrendsResult(deleted);
    }

    public async Task<ResetIntelligenceResult> ResetIntelligenceAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            _logger.LogWarning("Reset intelligence requested without confirmation. No rows deleted.");
            return new ResetIntelligenceResult(0);
        }

        var deleted = await _dbContext.IntelligenceItems.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return new ResetIntelligenceResult(deleted);
    }

    public async Task<ResetInvalidIntelligenceResult> ResetInvalidIntelligenceAsync(bool confirmed, CancellationToken cancellationToken)
    {
        var invalidDetails = await (
            from item in _dbContext.IntelligenceItems.AsNoTracking()
            join raw in _dbContext.RawSourceItems.AsNoTracking() on item.SourceItemId equals raw.Id
            join source in _dbContext.SourceDefinitions.AsNoTracking() on raw.SourceDefinitionId equals source.Id into sourceJoin
            from source in sourceJoin.DefaultIfEmpty()
            where raw.SourceClass != SourceClass.CurrentOfficial
            select new InvalidIntelligenceDetail(
                item.Id,
                source != null ? source.Name : "Unknown source",
                raw.SourceClass,
                item.SourceUrl))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!confirmed)
        {
            _logger.LogWarning("Reset invalid intelligence requested without confirmation. No rows deleted.");
            var remainingCount = await _dbContext.IntelligenceItems.CountAsync(cancellationToken).ConfigureAwait(false);
            return new ResetInvalidIntelligenceResult(invalidDetails.Length, 0, remainingCount, invalidDetails);
        }

        var deleted = 0;
        if (invalidDetails.Length > 0)
        {
            var invalidIds = invalidDetails.Select(detail => detail.IntelligenceItemId).ToArray();
            deleted = await _dbContext.IntelligenceItems
                .Where(item => invalidIds.Contains(item.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var remainingValidCount = await _dbContext.IntelligenceItems.CountAsync(cancellationToken).ConfigureAwait(false);
        return new ResetInvalidIntelligenceResult(invalidDetails.Length, deleted, remainingValidCount, invalidDetails);
    }

    public async Task<ResetAnalysisResult> ResetAnalysisAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            _logger.LogWarning("Reset analysis requested without confirmation. No rows deleted.");
            return new ResetAnalysisResult(0, 0);
        }

        var deletedIntelligence = await _dbContext.IntelligenceItems.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var deletedTrends = await _dbContext.TrendEvidence.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return new ResetAnalysisResult(deletedIntelligence, deletedTrends);
    }
}
