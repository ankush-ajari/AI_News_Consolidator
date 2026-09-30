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

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
