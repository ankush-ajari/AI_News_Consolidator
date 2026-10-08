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

    // Retrieve a minimal projection of RawSourceItems by Ids. Caller should supply
    // the set of RawSourceItem Ids required for downstream operations so the
    // repository can avoid materializing large text columns (RawContent,
    // EnrichedContent) for the whole table.
    Task<IReadOnlyCollection<RawSourceItemSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
}
