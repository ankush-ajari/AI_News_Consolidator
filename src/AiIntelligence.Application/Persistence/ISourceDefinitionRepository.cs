using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Persistence;

public interface ISourceDefinitionRepository
{
    Task<SourceDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task UpsertAsync(SourceDefinition sourceDefinition, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SourceDefinition>> ListAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
