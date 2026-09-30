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

public sealed class IntelligenceAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeUnprocessedAsync_PersistsOnlyRelevantValidResults()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = new SourceDefinition(Guid.NewGuid(), "Source", "Vendor", SourceType.Api, SourceClass.CurrentOfficial, new Uri("https://example.com"), true);
        fixture.DbContext.SourceDefinitions.Add(source);
        var relevant = CreateRawItem(source.Id, "https://example.com/relevant", "AI framework release");
        var irrelevant = CreateRawItem(source.Id, "https://example.com/irrelevant", "Non AI advertisement");
        fixture.DbContext.RawSourceItems.AddRange(relevant, irrelevant);
        await fixture.DbContext.SaveChangesAsync();

        var service = new IntelligenceAnalysisService(
            new RawSourceRepository(fixture.DbContext),
            new SourceDefinitionRepository(fixture.DbContext),
            new IntelligenceRepository(fixture.DbContext),
            new StubExtractor(rawItem => rawItem.Id == relevant.Id),
            NullLogger<IntelligenceAnalysisService>.Instance);

        var result = await service.AnalyzeUnprocessedAsync(CancellationToken.None);

        Assert.Equal(2, result.ProcessedCount);
        Assert.Equal(1, result.PersistedCount);
        Assert.Equal(1, result.IrrelevantCount);
        Assert.Equal(0, result.SkippedNonCurrentOfficialCount);
        Assert.Single(await fixture.DbContext.IntelligenceItems.ToArrayAsync());
    }

    [Fact]
    public async Task AnalyzeUnprocessedAsync_SkipsNonCurrentOfficialItems()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var official = new SourceDefinition(Guid.NewGuid(), "Official", "Vendor", SourceType.Api, SourceClass.CurrentOfficial, new Uri("https://example.com/official"), true);
        var trend = new SourceDefinition(Guid.NewGuid(), "Trend", "Vendor", SourceType.WebPage, SourceClass.TrendResearch, new Uri("https://example.com/trend"), true);
        fixture.DbContext.SourceDefinitions.AddRange(official, trend);
        var officialItem = CreateRawItem(official.Id, "https://example.com/official", "AI platform release", SourceClass.CurrentOfficial);
        var trendItem = CreateRawItem(trend.Id, "https://example.com/trend", "Trend report item", SourceClass.TrendResearch);
        fixture.DbContext.RawSourceItems.AddRange(officialItem, trendItem);
        await fixture.DbContext.SaveChangesAsync();

        var service = new IntelligenceAnalysisService(
            new RawSourceRepository(fixture.DbContext),
            new SourceDefinitionRepository(fixture.DbContext),
            new IntelligenceRepository(fixture.DbContext),
            new StubExtractor(_ => true),
            NullLogger<IntelligenceAnalysisService>.Instance);

        var result = await service.AnalyzeUnprocessedAsync(CancellationToken.None);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.PersistedCount);
        Assert.Equal(0, result.IrrelevantCount);
        Assert.Equal(1, result.SkippedNonCurrentOfficialCount);
        var saved = Assert.Single(await fixture.DbContext.IntelligenceItems.ToArrayAsync());
        Assert.Equal(officialItem.Id, saved.SourceItemId);
        Assert.Equal(SourceClass.CurrentOfficial, saved.SourceClass);
    }

    [Fact]
    public async Task AnalyzeUnprocessedAsync_AppliesLimitAfterFiltering()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var official = new SourceDefinition(Guid.NewGuid(), "Official", "Vendor", SourceType.Api, SourceClass.CurrentOfficial, new Uri("https://example.com/official"), true);
        var trend = new SourceDefinition(Guid.NewGuid(), "Trend", "Vendor", SourceType.WebPage, SourceClass.TrendResearch, new Uri("https://example.com/trend"), true);
        fixture.DbContext.SourceDefinitions.AddRange(official, trend);
        fixture.DbContext.RawSourceItems.AddRange(
            CreateRawItem(trend.Id, "https://example.com/trend", "Trend report item", SourceClass.TrendResearch),
            CreateRawItem(official.Id, "https://example.com/official-1", "AI release", SourceClass.CurrentOfficial),
            CreateRawItem(official.Id, "https://example.com/official-2", "AI release 2", SourceClass.CurrentOfficial));
        await fixture.DbContext.SaveChangesAsync();

        var service = new IntelligenceAnalysisService(
            new RawSourceRepository(fixture.DbContext),
            new SourceDefinitionRepository(fixture.DbContext),
            new IntelligenceRepository(fixture.DbContext),
            new StubExtractor(_ => true),
            NullLogger<IntelligenceAnalysisService>.Instance);

        var result = await service.AnalyzeUnprocessedAsync(CancellationToken.None, limit: 1);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.PersistedCount);
        Assert.Equal(0, result.IrrelevantCount);
        Assert.Equal(1, result.SkippedNonCurrentOfficialCount);
        Assert.Single(await fixture.DbContext.IntelligenceItems.ToArrayAsync());
    }

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string url, string content, SourceClass sourceClass = SourceClass.CurrentOfficial) => new(
        Guid.NewGuid(),
        sourceDefinitionId,
        "Title",
        new Uri(url),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        content,
        new ContentHash(Guid.NewGuid().ToString("N")),
        sourceClass: sourceClass);

    private sealed class StubExtractor : IIntelligenceExtractor
    {
        private readonly Func<RawSourceItem, bool> _isRelevant;

        public StubExtractor(Func<RawSourceItem, bool> isRelevant)
        {
            _isRelevant = isRelevant;
        }

        public Task<IntelligenceExtractionResult> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
        {
            return Task.FromResult(new IntelligenceExtractionResult(
                _isRelevant(sourceItem),
                "Microsoft",
                "Agents",
                "Framework",
                "Agent Framework",
                "Summary",
                new[] { "Capability" },
                new[] { "Unknown" },
                "Unknown",
                0.8m,
                new[] { "Evidence" },
                Array.Empty<SuggestedPersonaRelevance>()));
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
