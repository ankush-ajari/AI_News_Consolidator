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

public sealed class TrendAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeTrendResearchAsync_DoesNotPersistCurrentOfficialAsTrendEvidence()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.CurrentOfficial);
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "Official AI release"));
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor());

        var result = await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        Assert.Equal(0, result.ProcessedCount);
        Assert.Equal(1, result.SkippedNonTrendResearchCount);
        Assert.Empty(await fixture.DbContext.TrendEvidence.ToArrayAsync());
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_PersistsTrendResearchEvidenceWithProvenance()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch);
        var rawItem = CreateRawItem(source.Id, "Trend report says AI adoption increased by 42% in 2026.");
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor());

        var result = await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.PersistedCount);
        var evidence = Assert.Single(await fixture.DbContext.TrendEvidence.ToArrayAsync());
        Assert.Equal(rawItem.Id, evidence.SourceItemId);
        Assert.Equal("2026", evidence.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.SourceContent, evidence.PeriodProvenance);
        Assert.Equal("42%", evidence.QuantitativeEvidence);
        Assert.Equal("Stanford AI Index", evidence.PublicationName);
        Assert.Equal(new Uri("https://example.com/report"), evidence.SourceUrl);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_DoesNotProcessResearchDiscovery()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.ResearchDiscovery);
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "Research paper discovery item."));
        await fixture.DbContext.SaveChangesAsync();
        var extractor = new StubTrendExtractor();

        var result = await CreateService(fixture, extractor).AnalyzeTrendResearchAsync(CancellationToken.None);

        Assert.Equal(0, result.ProcessedCount);
        Assert.Equal(1, result.SkippedNonTrendResearchCount);
        Assert.Equal(0, extractor.CallCount);
        Assert.Empty(await fixture.DbContext.TrendEvidence.ToArrayAsync());
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_PersistsMultipleTrendEvidenceForOneRawSourceItem()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch);
        var rawItem = CreateRawItem(source.Id, "Trend report has multiple trend statements.");
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor(itemCount: 2));

        var result = await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(2, result.PersistedCount);
        var evidence = await fixture.DbContext.TrendEvidence.ToArrayAsync();
        Assert.Equal(2, evidence.Length);
        Assert.All(evidence, item => Assert.Equal(rawItem.Id, item.SourceItemId));
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_UsesPublishedDate_WhenPeriodUnknown()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "GitHub Octoverse 2025");
        var rawItem = CreateRawItem(source.Id, "Trend report without explicit period.", publishedAt: new DateTimeOffset(new DateTime(2025, 10, 1), TimeSpan.Zero));
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor(period: "Unknown"));

        await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        var evidence = Assert.Single(await fixture.DbContext.TrendEvidence.ToArrayAsync());
        Assert.Equal("2025", evidence.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.PublicationDate, evidence.PeriodProvenance);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_UsesSourceMetadataYear_WhenNoPublishedDate()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "Stanford AI Index 2026 - Economy", url: "https://hai.stanford.edu/ai-index/2026-ai-index-report/economy");
        var rawItem = CreateRawItem(source.Id, "Trend report without explicit period.", publishedAt: null, fetchedAt: new DateTimeOffset(new DateTime(2026, 5, 1), TimeSpan.Zero));
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor(period: "Unknown"));

        await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        var evidence = Assert.Single(await fixture.DbContext.TrendEvidence.ToArrayAsync());
        Assert.Equal("2026", evidence.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.SourceMetadata, evidence.PeriodProvenance);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_UsesSourceNameYear_WhenMetadataOnly()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "GitHub Octoverse 2025", url: "https://github.blog/octoverse");
        var rawItem = CreateRawItem(source.Id, "Trend report without explicit period.", publishedAt: null, fetchedAt: new DateTimeOffset(new DateTime(2025, 11, 1), TimeSpan.Zero));
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor(period: "Unknown"));

        await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        var evidence = Assert.Single(await fixture.DbContext.TrendEvidence.ToArrayAsync());
        Assert.Equal("2025", evidence.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.SourceMetadata, evidence.PeriodProvenance);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_UsesUnknown_WhenNoDeterministicPeriod()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource(SourceClass.TrendResearch, name: "Trend Source", url: "https://example.com/report");
        var rawItem = CreateRawItem(source.Id, "Trend report without explicit period.", publishedAt: null, fetchedAt: new DateTimeOffset(new DateTime(2027, 1, 1), TimeSpan.Zero));
        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(rawItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = CreateService(fixture, new StubTrendExtractor(period: "Unknown"));

        await service.AnalyzeTrendResearchAsync(CancellationToken.None);

        var evidence = Assert.Single(await fixture.DbContext.TrendEvidence.ToArrayAsync());
        Assert.Equal("Unknown", evidence.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.Unknown, evidence.PeriodProvenance);
    }

    private static TrendAnalysisService CreateService(SqliteFixture fixture, ITrendEvidenceExtractor extractor) => new(
        new RawSourceRepository(fixture.DbContext),
        new SourceDefinitionRepository(fixture.DbContext),
        new TrendEvidenceRepository(fixture.DbContext),
        extractor,
        NullLogger<TrendAnalysisService>.Instance);

    private static SourceDefinition CreateSource(SourceClass sourceClass, string? name = null, string? url = null) => new(
        Guid.NewGuid(),
        name ?? (sourceClass == SourceClass.TrendResearch ? "Trend Source" : "Official Source"),
        "Vendor",
        SourceType.WebPage,
        sourceClass,
        new Uri(url ?? "https://example.com/report"),
        isEnabled: true);

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string content, DateTimeOffset? publishedAt = null, DateTimeOffset? fetchedAt = null) => new(
        Guid.NewGuid(),
        sourceDefinitionId,
        "Report",
        new Uri("https://example.com/report"),
        publishedAt,
        fetchedAt ?? DateTimeOffset.UtcNow,
        content,
        new ContentHash(Guid.NewGuid().ToString("N")));

    private sealed class StubTrendExtractor : ITrendEvidenceExtractor
    {
        private readonly int _itemCount;
        private readonly string _period;

        public StubTrendExtractor(int itemCount = 1, string period = "2026")
        {
            _itemCount = itemCount;
            _period = period;
        }

        public int CallCount { get; private set; }

        public Task<TrendEvidenceExtractionBatch> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
        {
            CallCount++;
            var provenance = string.Equals(_period, "Unknown", StringComparison.OrdinalIgnoreCase)
                ? TrendEvidencePeriodProvenance.Unknown
                : TrendEvidencePeriodProvenance.SourceContent;
            var items = Enumerable.Range(1, _itemCount)
                .Select(index => new TrendEvidenceExtractionResult(
                    index == 1 ? "Enterprise AI adoption" : "Model inference cost reduction",
                    _period,
                    provenance,
                    index == 1 ? "Enterprise AI adoption increased." : "Inference costs decreased.",
                    index == 1 ? "42%" : "Unknown",
                    "The publication reported an adoption trend.",
                    0.8m,
                    new Uri("https://example.com/report"),
                    "Stanford AI Index"))
                .ToArray();

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
