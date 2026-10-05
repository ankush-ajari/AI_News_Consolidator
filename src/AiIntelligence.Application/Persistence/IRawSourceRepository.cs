using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Persistence;

public interface IRawSourceRepository
{
    Task<RawSourceItem?> FindByCanonicalUrlAndHashAsync(
        string canonicalUrl,
        string contentHash,
        CancellationToken cancellationToken);

    Task<RawSourceItem?> FindBySourceDefinitionAndCanonicalUrlAsync(
        Guid sourceDefinitionId,
        string canonicalUrl,
        CancellationToken cancellationToken);

    Task AddAsync(RawSourceItem item, CancellationToken cancellationToken);

    Task UpdateAsync(RawSourceItem item, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RawSourceItem>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RawSourceItem>> ListUnprocessedAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
