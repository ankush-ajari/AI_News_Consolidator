using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Persistence;

public interface ITrendEvidenceRepository
{
    Task<bool> ExistsForSourceItemAsync(Guid sourceItemId, CancellationToken cancellationToken);

    Task AddAsync(TrendEvidence item, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TrendEvidence>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TrendEvidence>> ListAsync(int? limit, CancellationToken cancellationToken);

    IAsyncEnumerable<TrendEvidenceCandidate> StreamCandidatesForCorrelationAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TrendEvidenceCandidate>> GetCandidatesByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    // Retrieve trend evidence by a set of source URLs. Used to avoid loading
    // the entire TrendEvidence table when only a bounded subset is required.
    Task<IReadOnlyCollection<TrendEvidence>> GetBySourceUrlsAsync(
        IReadOnlyCollection<string> sourceUrls,
        CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
