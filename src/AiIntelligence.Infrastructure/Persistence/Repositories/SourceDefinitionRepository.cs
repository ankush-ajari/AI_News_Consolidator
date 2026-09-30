using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AiIntelligence.Infrastructure.Persistence.Repositories;

public sealed class SourceDefinitionRepository : ISourceDefinitionRepository
{
    private readonly AiIntelligenceDbContext _dbContext;

    public SourceDefinitionRepository(AiIntelligenceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SourceDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.SourceDefinitions.AsNoTracking().FirstOrDefaultAsync(source => source.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(SourceDefinition sourceDefinition, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SourceDefinitions.FindAsync(new object[] { sourceDefinition.Id }, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _dbContext.SourceDefinitions.AddAsync(sourceDefinition, cancellationToken).ConfigureAwait(false);
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(sourceDefinition);
    }

    public async Task<IReadOnlyCollection<SourceDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.SourceDefinitions.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
