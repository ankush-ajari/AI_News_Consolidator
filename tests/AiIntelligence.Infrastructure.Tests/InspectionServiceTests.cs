using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.Inspection;
using AiIntelligence.Infrastructure.Persistence;
using AiIntelligence.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class InspectionServiceTests
{
    [Fact]
    public async Task GetStatsAsync_GroupsRawItemsBySource_AndReportsAllSourceClasses()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var official = await AddSourceAsync(fixture, "Official", SourceClass.CurrentOfficial);
        var trend = await AddSourceAsync(fixture, "Trend", SourceClass.TrendResearch);
        var discovery = await AddSourceAsync(fixture, "Discovery", SourceClass.ResearchDiscovery);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(official, "https://example.com/1", "official content"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(trend, "https://example.com/2", "trend content"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(discovery, "https://example.com/3", "research discovery content"));
        await fixture.DbContext.SaveChangesAsync();

        var stats = await new InspectionService(fixture.DbContext).GetStatsAsync(CancellationToken.None);

        Assert.Equal(1, stats.CurrentOfficialCount);
        Assert.Equal(1, stats.TrendResearchCount);
        Assert.Equal(1, stats.ResearchDiscoveryCount);
        Assert.Contains(stats.RawItemsBySource, row => row.SourceName == "Official" && row.SourceClass == SourceClass.CurrentOfficial && row.Count == 1);
        Assert.Contains(stats.RawItemsBySource, row => row.SourceName == "Trend" && row.SourceClass == SourceClass.TrendResearch && row.Count == 1);
        Assert.Contains(stats.RawItemsBySource, row => row.SourceName == "Discovery" && row.SourceClass == SourceClass.ResearchDiscovery && row.Count == 1);
    }

    [Fact]
    public async Task GetSourcesAsync_UsesConfiguredSources_NotOnlyPersistedSources()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var official = new SourceDefinition(Guid.NewGuid(), "Official", "Vendor", SourceType.WebPage, SourceClass.CurrentOfficial, new Uri("https://example.com/official"), true);
        var trend = new SourceDefinition(Guid.NewGuid(), "Trend Config Only", "Vendor", SourceType.WebPage, SourceClass.TrendResearch, new Uri("https://example.com/trend"), true);
        fixture.DbContext.SourceDefinitions.Add(official);
        await fixture.DbContext.SaveChangesAsync();

        var rows = await new InspectionService(fixture.DbContext).GetSourcesAsync(new[] { official, trend }, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.SourceName == "Trend Config Only" && row.SourceClass == SourceClass.TrendResearch);
    }

    [Fact]
    public async Task GetRawAsync_AppliesClassFilter()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var official = await AddSourceAsync(fixture, "Official", SourceClass.CurrentOfficial);
        var trend = await AddSourceAsync(fixture, "Trend", SourceClass.TrendResearch);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(official, "https://example.com/1", "official content"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(trend, "https://example.com/2", "trend content"));
        await fixture.DbContext.SaveChangesAsync();

        var rows = await new InspectionService(fixture.DbContext).GetRawAsync(new RawInspectionFilter(SourceClass: SourceClass.TrendResearch), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Trend", row.SourceName);
        Assert.Equal(SourceClass.TrendResearch, row.SourceClass);
    }

    [Fact]
    public async Task GetRawAsync_AppliesLimit()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = await AddSourceAsync(fixture, "Official", SourceClass.CurrentOfficial);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/1", "content 1"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/2", "content 2"));
        await fixture.DbContext.SaveChangesAsync();

        var rows = await new InspectionService(fixture.DbContext).GetRawAsync(new RawInspectionFilter(Limit: 1), CancellationToken.None);

        Assert.Single(rows);
    }

    [Fact]
    public async Task GetStatsAsync_CalculatesContentLengthStatisticsIncludingEmptyContent()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = await AddSourceAsync(fixture, "Official", SourceClass.CurrentOfficial);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/1", ""));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/2", new string('a', 600)));
        await fixture.DbContext.SaveChangesAsync();

        var stats = await new InspectionService(fixture.DbContext).GetStatsAsync(CancellationToken.None);

        Assert.Equal(0, stats.ContentQuality.MinimumRawContentLength);
        Assert.Equal(300, stats.ContentQuality.AverageRawContentLength);
        Assert.Equal(600, stats.ContentQuality.MaximumRawContentLength);
        Assert.Equal(1, stats.ContentQuality.CountRawContentLessThan300);
        Assert.Equal(1, stats.ContentQuality.CountRawContentNullOrEmpty);
    }

    [Fact]
    public async Task InspectionMethods_AreReadOnly()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = await AddSourceAsync(fixture, "Official", SourceClass.CurrentOfficial);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/1", "content"));
        await fixture.DbContext.SaveChangesAsync();
        var before = await fixture.DbContext.RawSourceItems.CountAsync();
        var service = new InspectionService(fixture.DbContext);

        await service.GetSourcesAsync(CancellationToken.None);
        await service.GetRawAsync(new RawInspectionFilter(), CancellationToken.None);
        await service.GetTrendsAsync(new TrendInspectionFilter(), CancellationToken.None);
        await service.GetStatsAsync(CancellationToken.None);

        var after = await fixture.DbContext.RawSourceItems.CountAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_AppliesLimitBeforeExtractorInvocation()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = await AddSourceAsync(fixture, "Trend", SourceClass.TrendResearch);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/1", "trend one"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(source.Id, "https://example.com/2", "trend two"));
        await fixture.DbContext.SaveChangesAsync();
        var extractor = new CountingTrendExtractor();
        var service = new TrendAnalysisService(
            new RawSourceRepository(fixture.DbContext),
            new SourceDefinitionRepository(fixture.DbContext),
            new TrendEvidenceRepository(fixture.DbContext),
            extractor,
            NullLogger<TrendAnalysisService>.Instance);

        var result = await service.AnalyzeTrendResearchAsync(CancellationToken.None, limit: 1);

        Assert.Equal(1, extractor.CallCount);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.PersistedCount);
        Assert.Equal(0, result.SkippedNonTrendResearchCount);
    }

    [Fact]
    public async Task AnalyzeTrendResearchAsync_AppliesLimitAfterFiltering()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var trend = await AddSourceAsync(fixture, "Trend", SourceClass.TrendResearch);
        var official = await AddSourceAsync(fixture, "Official", SourceClass.CurrentOfficial);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(official, "https://example.com/official", "official content"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(trend, "https://example.com/trend-1", "trend one"));
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(trend, "https://example.com/trend-2", "trend two"));
        await fixture.DbContext.SaveChangesAsync();
        var extractor = new CountingTrendExtractor();
        var service = new TrendAnalysisService(
            new RawSourceRepository(fixture.DbContext),
            new SourceDefinitionRepository(fixture.DbContext),
            new TrendEvidenceRepository(fixture.DbContext),
            extractor,
            NullLogger<TrendAnalysisService>.Instance);

        var result = await service.AnalyzeTrendResearchAsync(CancellationToken.None, limit: 1);

        Assert.Equal(1, extractor.CallCount);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.PersistedCount);
    }

    private static async Task<SourceDefinition> AddSourceAsync(SqliteFixture fixture, string name, SourceClass sourceClass)
    {
        var source = new SourceDefinition(Guid.NewGuid(), name, "Vendor", SourceType.WebPage, sourceClass, new Uri("https://example.com"), true);
        fixture.DbContext.SourceDefinitions.Add(source);
        await fixture.DbContext.SaveChangesAsync();
        return source;
    }

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string url, string content) => new(
        Guid.NewGuid(),
        sourceDefinitionId,
        "Title",
        new Uri(url),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        content,
        new ContentHash(Guid.NewGuid().ToString("N")));

    private static RawSourceItem CreateRawItem(SourceDefinition sourceDefinition, string url, string content) => new(
        Guid.NewGuid(),
        sourceDefinition.Id,
        "Title",
        new Uri(url),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        content,
        new ContentHash(Guid.NewGuid().ToString("N")),
        sourceClass: sourceDefinition.SourceClass);

    private sealed class CountingTrendExtractor : ITrendEvidenceExtractor
    {
        public int CallCount { get; private set; }

        public Task<TrendEvidenceExtractionBatch> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TrendEvidenceExtractionBatch(new[]
            {
                new TrendEvidenceExtractionResult("Topic", "2026", TrendEvidencePeriodProvenance.SourceContent, "Finding", "Unknown", "Summary", 0.8m, sourceItem.Url, "Publication")
            }));
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
