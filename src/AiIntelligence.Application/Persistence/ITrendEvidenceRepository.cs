using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Persistence;

public interface ITrendEvidenceRepository
{
    Task<bool> ExistsForSourceItemAsync(Guid sourceItemId, CancellationToken cancellationToken);

    Task AddAsync(TrendEvidence item, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TrendEvidence>> ListAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
