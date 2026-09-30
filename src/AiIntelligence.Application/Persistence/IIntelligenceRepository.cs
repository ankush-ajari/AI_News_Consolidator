using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Persistence;

public interface IIntelligenceRepository
{
    Task<bool> ExistsForSourceItemAsync(Guid sourceItemId, CancellationToken cancellationToken);

    Task AddAsync(IntelligenceItem item, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<IntelligenceItem>> ListAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
