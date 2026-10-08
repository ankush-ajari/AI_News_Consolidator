using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AiIntelligence.Infrastructure.Persistence.Repositories;

public sealed class RawSourceRepository : IRawSourceRepository
{
    private readonly AiIntelligenceDbContext _dbContext;

    public RawSourceRepository(AiIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<RawSourceItem?> FindByCanonicalUrlAndHashAsync(
        string canonicalUrl,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var hash = new ContentHash(contentHash);
        return _dbContext.RawSourceItems.FirstOrDefaultAsync(
            item => item.CanonicalUrl == canonicalUrl && item.ContentHash == hash,
            cancellationToken);
    }

    public Task<RawSourceItem?> FindBySourceDefinitionAndCanonicalUrlAsync(
        Guid sourceDefinitionId,
        string canonicalUrl,
        CancellationToken cancellationToken)
    {
        return _dbContext.RawSourceItems.FirstOrDefaultAsync(
            item => item.SourceDefinitionId == sourceDefinitionId && item.CanonicalUrl == canonicalUrl,
            cancellationToken);
    }

    public async Task AddAsync(RawSourceItem item, CancellationToken cancellationToken)
    {
        await _dbContext.RawSourceItems.AddAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateAsync(RawSourceItem item, CancellationToken cancellationToken)
    {
        _dbContext.RawSourceItems.Update(item);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<RawSourceItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.RawSourceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<RawSourceItemSummary>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count == 0)
        {
            return Array.Empty<RawSourceItemSummary>();
        }

        return await _dbContext.RawSourceItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new RawSourceItemSummary(
                item.Id,
                item.PublishedAt,
                item.FetchedAt,
                item.CanonicalUrl,
                item.SourceDefinitionId,
                item.SourceClass))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<RawSourceItem>> ListUnprocessedAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.RawSourceItems
            .AsNoTracking()
            .Where(raw => !_dbContext.IntelligenceItems.Any(item => item.SourceItemId == raw.Id))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
