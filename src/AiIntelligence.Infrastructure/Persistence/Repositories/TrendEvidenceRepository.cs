using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

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

    public async Task<IReadOnlyCollection<TrendEvidence>> ListAsync(int? limit, CancellationToken cancellationToken)
    {
        var query = _dbContext.TrendEvidence.AsNoTracking().AsQueryable();
        if (limit is > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TrendEvidenceCandidate> StreamCandidatesForCorrelationAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = _dbContext.TrendEvidence
            .AsNoTracking()
            .Where(trend => _dbContext.RawSourceItems.Any(raw =>
                raw.Id == trend.SourceItemId
                && raw.SourceClass == AiIntelligence.Domain.Enums.SourceClass.TrendResearch))
            .OrderBy(trend => trend.Id)
            .Select(trend => new TrendEvidenceCandidate(
                trend.Id,
                trend.SourceItemId,
                trend.Topic,
                trend.Period,
                trend.PeriodProvenance,
                trend.Finding,
                trend.EvidenceSummary,
                trend.Confidence,
                trend.SourceUrl,
                trend.PublicationName,
                trend.ConceptTags,
                trend.TrendFamily));

        await foreach (var candidate in query
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return candidate;
        }
    }

    public async Task<IReadOnlyCollection<TrendEvidenceCandidate>> GetCandidatesByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count == 0)
        {
            return Array.Empty<TrendEvidenceCandidate>();
        }

        return await _dbContext.TrendEvidence
            .AsNoTracking()
            .Where(trend => ids.Contains(trend.Id)
                && _dbContext.RawSourceItems.Any(raw =>
                    raw.Id == trend.SourceItemId
                    && raw.SourceClass == AiIntelligence.Domain.Enums.SourceClass.TrendResearch))
            .Select(trend => new TrendEvidenceCandidate(
                trend.Id,
                trend.SourceItemId,
                trend.Topic,
                trend.Period,
                trend.PeriodProvenance,
                trend.Finding,
                trend.EvidenceSummary,
                trend.Confidence,
                trend.SourceUrl,
                trend.PublicationName,
                trend.ConceptTags,
                trend.TrendFamily))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<TrendEvidence>> GetBySourceUrlsAsync(IReadOnlyCollection<string> sourceUrls, CancellationToken cancellationToken)
    {
        if (sourceUrls is null || sourceUrls.Count == 0)
        {
            return Array.Empty<TrendEvidence>();
        }

        return await _dbContext.TrendEvidence
            .AsNoTracking()
            .Where(trend => sourceUrls.Contains(trend.SourceUrl.AbsoluteUri))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
