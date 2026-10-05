using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.Persistence;
using AiIntelligence.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class TrendFamilyPipelineTests
{
    [Fact]
    public async Task AnalyzeTrendResearchAsync_ClassifiesFamilyBeforeConsolidation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "Stanford AI Index 2026 - Technical Performance", url: "https://hai.stanford.edu/ai-index/2026-ai-index-report/technical-performance");
        var rawItem = CreateRawItem(source.Id, "OSWorld task completion gains for AI agents.");
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var extractor = new StubTrendExtractor();
        var service = CreateService(fixture, extractor);

        await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        var evidence = await fixture.DbContext.TrendEvidence.ToArrayAsync();
        Assert.Single(evidence);
        Assert.Equal(TrendFamily.AgentTaskPerformance, evidence[0].TrendFamily);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_PrefersSpecificFamilyOverUnknown()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "Stanford AI Index 2026 - Technical Performance", url: "https://hai.stanford.edu/ai-index/2026-ai-index-report/technical-performance");
        var rawItem = CreateRawItem(source.Id, "OSWorld task completion gains for AI agents.");
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var extractor = new StubTrendExtractor(returnUnknown: true);
        var service = CreateService(fixture, extractor);

        await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        var evidence = await fixture.DbContext.TrendEvidence.ToArrayAsync();
        Assert.Single(evidence);
        Assert.NotEqual(TrendFamily.Unknown, evidence[0].TrendFamily);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_DoesNotMergeDistinctFamilies()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "Stanford AI Index 2026 - Technical Performance", url: "https://hai.stanford.edu/ai-index/2026-ai-index-report/technical-performance");
        var rawItem = CreateRawItem(source.Id, "Model gap items.");
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var extractor = new StubTrendExtractor(includeGapPair: true);
        var service = CreateService(fixture, extractor);

        var result = await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        Assert.Equal(3, result.PersistedCount);
        var evidence = await fixture.DbContext.TrendEvidence.ToArrayAsync();
        Assert.Contains(evidence, item => item.TrendFamily == TrendFamily.AgentTaskPerformance);
        Assert.Contains(evidence, item => item.TrendFamily == TrendFamily.USChinaModelGap);
        Assert.Contains(evidence, item => item.TrendFamily == TrendFamily.OpenClosedModelGap);
    }

    private static TrendAnalysisService CreateService(SqliteFixture fixture, ITrendEvidenceExtractor extractor) => new(
        new RawSourceRepository(fixture.DbContext),
        new SourceDefinitionRepository(fixture.DbContext),
        new TrendEvidenceRepository(fixture.DbContext),
        extractor,
        NullLogger<TrendAnalysisService>.Instance);

    private static SourceDefinition CreateSource(SourceClass sourceClass, string name, string url) => new(
        Guid.NewGuid(),
        name,
        "Vendor",
        SourceType.WebPage,
        sourceClass,
        new Uri(url),
        isEnabled: true);

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string content) => new(
        Guid.NewGuid(),
        sourceDefinitionId,
        "Report",
        new Uri("https://example.com/report"),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        content,
        new ContentHash(Guid.NewGuid().ToString("N")));

    private sealed class StubTrendExtractor : ITrendEvidenceExtractor
    {
        private readonly bool _returnUnknown;
        private readonly bool _includeGapPair;

        public StubTrendExtractor(bool returnUnknown = false, bool includeGapPair = false)
        {
            _returnUnknown = returnUnknown;
            _includeGapPair = includeGapPair;
        }

        public Task<TrendEvidenceExtractionBatch> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
        {
            var items = new List<TrendEvidenceExtractionResult>
            {
                new(
                    "AI agent task performance gains",
                    "2026",
                    TrendEvidencePeriodProvenance.SourceContent,
                    "Agent task completion gains on OSWorld",
                    "42%",
                    "Evidence summary",
                    0.8m,
                    new Uri("https://example.com/report"),
                    "Stanford AI Index",
                    Array.Empty<AIConceptTag>())
            };

            if (_returnUnknown)
            {
                items.Add(new TrendEvidenceExtractionResult(
                    "Generative AI adoption by consumers",
                    "2026",
                    TrendEvidencePeriodProvenance.SourceContent,
                    "Adoption reached a majority of users.",
                    "42%",
                    "Evidence summary",
                    0.8m,
                    new Uri("https://example.com/report"),
                    "Stanford AI Index",
                    Array.Empty<AIConceptTag>()));
            }

            if (_includeGapPair)
            {
                items.Add(new TrendEvidenceExtractionResult(
                    "U.S.-China model performance gap",
                    "2026",
                    TrendEvidencePeriodProvenance.SourceContent,
                    "US-China model performance convergence",
                    "42%",
                    "Evidence summary",
                    0.8m,
                    new Uri("https://example.com/report"),
                    "Stanford AI Index",
                    Array.Empty<AIConceptTag>()));
                items.Add(new TrendEvidenceExtractionResult(
                    "Open vs. closed model performance gap",
                    "2026",
                    TrendEvidencePeriodProvenance.SourceContent,
                    "Closed models regain lead over open models",
                    "42%",
                    "Evidence summary",
                    0.8m,
                    new Uri("https://example.com/report"),
                    "Stanford AI Index",
                    Array.Empty<AIConceptTag>()));
            }

            return Task.FromResult(new TrendEvidenceExtractionBatch(items));
        }
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(SqliteConnection connection, AiIntelligenceDbContext dbContext)
        {
            Connection = connection;
            DbContext = dbContext;
        }

        public SqliteConnection Connection { get; }

        public AiIntelligenceDbContext DbContext { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AiIntelligenceDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AiIntelligenceDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
