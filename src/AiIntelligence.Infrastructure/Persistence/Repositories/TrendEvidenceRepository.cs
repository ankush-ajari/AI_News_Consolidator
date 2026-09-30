using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AiIntelligence.Infrastructure.Persistence.Repositories;

public sealed class TrendEvidenceRepository : ITrendEvidenceRepository
{
    private readonly AiIntelligenceDbContext _dbContext;

    public TrendEvidenceRepository(AiIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsForSourceItemAsync(Guid sourceItemId, CancellationToken cancellationToken)
    {
        return _dbContext.TrendEvidence.AnyAsync(item => item.SourceItemId == sourceItemId, cancellationToken);
    }

    public async Task AddAsync(TrendEvidence item, CancellationToken cancellationToken)
    {
        await _dbContext.TrendEvidence.AddAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<TrendEvidence>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.TrendEvidence.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
