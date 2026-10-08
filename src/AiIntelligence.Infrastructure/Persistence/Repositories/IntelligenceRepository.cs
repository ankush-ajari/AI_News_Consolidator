using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AiIntelligence.Infrastructure.Persistence.Repositories;

public sealed class IntelligenceRepository : IIntelligenceRepository
{
    private readonly AiIntelligenceDbContext _dbContext;

    public IntelligenceRepository(AiIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsForSourceItemAsync(Guid sourceItemId, CancellationToken cancellationToken)
    {
        return _dbContext.IntelligenceItems.AnyAsync(item => item.SourceItemId == sourceItemId, cancellationToken);
    }

    public async Task AddAsync(IntelligenceItem item, CancellationToken cancellationToken)
    {
        await _dbContext.IntelligenceItems.AddAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<IntelligenceItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.IntelligenceItems.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<IntelligenceItem>> ListAsync(int? limit, CancellationToken cancellationToken)
    {
        var query = _dbContext.IntelligenceItems.AsNoTracking().AsQueryable();
        if (limit is > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<IntelligenceItem>> ListForCorrelationAsync(
        IReadOnlyCollection<Guid> selectedSourceIds,
        int? limit,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.IntelligenceItems
            .AsNoTracking()
            .Where(item => item.SourceClass == AiIntelligence.Domain.Enums.SourceClass.CurrentOfficial);

        if (selectedSourceIds.Count > 0)
        {
            IQueryable<IntelligenceItem> selectedQuery = query
                .Where(item => selectedSourceIds.Contains(item.SourceItemId))
                .OrderBy(item => item.Id);
            if (limit is > 0)
            {
                selectedQuery = selectedQuery.Take(limit.Value);
            }

            var selected = await selectedQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            if (selected.Length > 0)
            {
                return selected;
            }
        }

        query = query.Where(item => !item.Summary.ToLower().Contains("mock")
            && item.Topic.ToLower() != "ai technology development");
        query = query.OrderBy(item => item.Id);
        if (limit is > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<IntelligenceItem>> ListForPersonaAsync(int? limit, CancellationToken cancellationToken)
    {
        IQueryable<IntelligenceItem> query = _dbContext.IntelligenceItems
            .AsNoTracking()
            .Where(item => item.SourceClass == AiIntelligence.Domain.Enums.SourceClass.CurrentOfficial)
            .OrderBy(item => item.Id);
        if (limit is > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
