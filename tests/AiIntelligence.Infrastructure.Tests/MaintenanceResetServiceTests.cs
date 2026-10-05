using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.Maintenance;
using AiIntelligence.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class MaintenanceResetServiceTests
{
    [Fact]
    public async Task ResetTrendsAsync_DeletesOnlyTrendEvidence()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var service = new MaintenanceResetService(fixture.DbContext, NullLogger<MaintenanceResetService>.Instance);

        var result = await service.ResetTrendsAsync(confirmed: true, CancellationToken.None);

        Assert.Equal(1, result.DeletedTrendEvidenceCount);
        Assert.Equal(0, await fixture.DbContext.TrendEvidence.CountAsync());
        Assert.Equal(1, await fixture.DbContext.IntelligenceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.RawSourceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.SourceDefinitions.CountAsync());
    }

    [Fact]
    public async Task ResetIntelligenceAsync_DeletesOnlyIntelligenceItems()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var service = new MaintenanceResetService(fixture.DbContext, NullLogger<MaintenanceResetService>.Instance);

        var result = await service.ResetIntelligenceAsync(confirmed: true, CancellationToken.None);

        Assert.Equal(1, result.DeletedIntelligenceItemsCount);
        Assert.Equal(1, await fixture.DbContext.TrendEvidence.CountAsync());
        Assert.Equal(0, await fixture.DbContext.IntelligenceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.RawSourceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.SourceDefinitions.CountAsync());
    }

    [Fact]
    public async Task ResetAnalysisAsync_DeletesIntelligenceAndTrendEvidenceOnly()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var service = new MaintenanceResetService(fixture.DbContext, NullLogger<MaintenanceResetService>.Instance);

        var result = await service.ResetAnalysisAsync(confirmed: true, CancellationToken.None);

        Assert.Equal(1, result.DeletedIntelligenceItemsCount);
        Assert.Equal(1, result.DeletedTrendEvidenceCount);
        Assert.Equal(0, await fixture.DbContext.TrendEvidence.CountAsync());
        Assert.Equal(0, await fixture.DbContext.IntelligenceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.RawSourceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.SourceDefinitions.CountAsync());
    }

    [Fact]
    public async Task ResetCommands_DoNothingWithoutConfirm()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedAsync(fixture);
        var service = new MaintenanceResetService(fixture.DbContext, NullLogger<MaintenanceResetService>.Instance);

        var trends = await service.ResetTrendsAsync(confirmed: false, CancellationToken.None);
        var intelligence = await service.ResetIntelligenceAsync(confirmed: false, CancellationToken.None);
        var analysis = await service.ResetAnalysisAsync(confirmed: false, CancellationToken.None);
        var invalid = await service.ResetInvalidIntelligenceAsync(confirmed: false, CancellationToken.None);

        Assert.Equal(0, trends.DeletedTrendEvidenceCount);
        Assert.Equal(0, intelligence.DeletedIntelligenceItemsCount);
        Assert.Equal(0, analysis.DeletedIntelligenceItemsCount);
        Assert.Equal(0, analysis.DeletedTrendEvidenceCount);
        Assert.Equal(1, invalid.InvalidIntelligenceCount);
        Assert.Equal(0, invalid.DeletedCount);
        Assert.Equal(1, await fixture.DbContext.TrendEvidence.CountAsync());
        Assert.Equal(1, await fixture.DbContext.IntelligenceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.RawSourceItems.CountAsync());
        Assert.Equal(1, await fixture.DbContext.SourceDefinitions.CountAsync());
    }

    [Fact]
    public async Task ResetInvalidIntelligenceAsync_DeletesOnlyNonCurrentOfficialItems()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedInvalidIntelligenceAsync(fixture);
        var service = new MaintenanceResetService(fixture.DbContext, NullLogger<MaintenanceResetService>.Instance);

        var result = await service.ResetInvalidIntelligenceAsync(confirmed: true, CancellationToken.None);

        Assert.Equal(2, result.InvalidIntelligenceCount);
        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(1, result.RemainingValidCount);
        Assert.Equal(1, await fixture.DbContext.IntelligenceItems.CountAsync());
        Assert.Equal(3, await fixture.DbContext.RawSourceItems.CountAsync());
        Assert.Equal(3, await fixture.DbContext.SourceDefinitions.CountAsync());
        Assert.Equal(1, await fixture.DbContext.TrendEvidence.CountAsync());
        Assert.DoesNotContain(await fixture.DbContext.IntelligenceItems.ToArrayAsync(), item => item.SourceClass != SourceClass.CurrentOfficial);
    }

    [Fact]
    public async Task ResetInvalidIntelligenceAsync_ReportsDeletionsWithoutConfirm()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        await SeedInvalidIntelligenceAsync(fixture);
        var service = new MaintenanceResetService(fixture.DbContext, NullLogger<MaintenanceResetService>.Instance);

        var result = await service.ResetInvalidIntelligenceAsync(confirmed: false, CancellationToken.None);

        Assert.Equal(2, result.InvalidIntelligenceCount);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(3, result.RemainingValidCount);
        Assert.Equal(3, await fixture.DbContext.IntelligenceItems.CountAsync());
        Assert.Equal(3, await fixture.DbContext.RawSourceItems.CountAsync());
        Assert.Equal(3, await fixture.DbContext.SourceDefinitions.CountAsync());
        Assert.Equal(1, await fixture.DbContext.TrendEvidence.CountAsync());
    }

    private static async Task SeedAsync(SqliteFixture fixture)
    {
        var source = new SourceDefinition(Guid.NewGuid(), "Source", "Vendor", SourceType.WebPage, SourceClass.TrendResearch, new Uri("https://example.com"), true);
        var raw = new RawSourceItem(Guid.NewGuid(), source.Id, "Raw", new Uri("https://example.com/raw"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "raw content", new ContentHash("hash"), sourceClass: source.SourceClass);
        var intelligence = new IntelligenceItem(Guid.NewGuid(), raw.Id, "Vendor", "Topic", "Category", "Framework", "Summary", Array.Empty<string>(), Array.Empty<string>(), "Unknown", SourceClass.TrendResearch, DateTimeOffset.UtcNow, raw.Url, new[] { AIConceptTag.ModelCapability });
        var trend = new TrendEvidence(Guid.NewGuid(), raw.Id, "Topic", "2026", TrendEvidencePeriodProvenance.SourceContent, "Finding", "Evidence", 0.8m, raw.Url, "42%", "Publication", new[] { AIConceptTag.ModelCapability });

        fixture.DbContext.SourceDefinitions.Add(source);
        fixture.DbContext.RawSourceItems.Add(raw);
        fixture.DbContext.IntelligenceItems.Add(intelligence);
        fixture.DbContext.TrendEvidence.Add(trend);
        await fixture.DbContext.SaveChangesAsync();
    }

    private static async Task SeedInvalidIntelligenceAsync(SqliteFixture fixture)
    {
        var officialSource = new SourceDefinition(Guid.NewGuid(), "Official", "Vendor", SourceType.WebPage, SourceClass.CurrentOfficial, new Uri("https://example.com/official"), true);
        var trendSource = new SourceDefinition(Guid.NewGuid(), "Trend", "Vendor", SourceType.WebPage, SourceClass.TrendResearch, new Uri("https://example.com/trend"), true);
        var discoverySource = new SourceDefinition(Guid.NewGuid(), "Discovery", "Vendor", SourceType.WebPage, SourceClass.ResearchDiscovery, new Uri("https://example.com/discovery"), true);

        var officialRaw = new RawSourceItem(Guid.NewGuid(), officialSource.Id, "Official Raw", new Uri("https://example.com/official/raw"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "official", new ContentHash("official"), sourceClass: officialSource.SourceClass);
        var trendRaw = new RawSourceItem(Guid.NewGuid(), trendSource.Id, "Trend Raw", new Uri("https://example.com/trend/raw"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "trend", new ContentHash("trend"), sourceClass: trendSource.SourceClass);
        var discoveryRaw = new RawSourceItem(Guid.NewGuid(), discoverySource.Id, "Discovery Raw", new Uri("https://example.com/discovery/raw"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "discovery", new ContentHash("discovery"), sourceClass: discoverySource.SourceClass);

        var officialIntelligence = new IntelligenceItem(Guid.NewGuid(), officialRaw.Id, "Vendor", "Topic", "Category", "Framework", "Summary", Array.Empty<string>(), Array.Empty<string>(), "Unknown", SourceClass.CurrentOfficial, DateTimeOffset.UtcNow, officialRaw.Url, new[] { AIConceptTag.DeveloperPlatform });
        var trendIntelligence = new IntelligenceItem(Guid.NewGuid(), trendRaw.Id, "Vendor", "Topic", "Category", "Framework", "Summary", Array.Empty<string>(), Array.Empty<string>(), "Unknown", SourceClass.TrendResearch, DateTimeOffset.UtcNow, trendRaw.Url, new[] { AIConceptTag.EnterpriseAdoption });
        var discoveryIntelligence = new IntelligenceItem(Guid.NewGuid(), discoveryRaw.Id, "Vendor", "Topic", "Category", "Framework", "Summary", Array.Empty<string>(), Array.Empty<string>(), "Unknown", SourceClass.ResearchDiscovery, DateTimeOffset.UtcNow, discoveryRaw.Url, new[] { AIConceptTag.WorkforceImpact });

        var trendEvidence = new TrendEvidence(Guid.NewGuid(), trendRaw.Id, "Topic", "2026", TrendEvidencePeriodProvenance.SourceContent, "Finding", "Evidence", 0.8m, trendRaw.Url, "42%", "Publication", new[] { AIConceptTag.EnterpriseAdoption });

        fixture.DbContext.SourceDefinitions.AddRange(officialSource, trendSource, discoverySource);
        fixture.DbContext.RawSourceItems.AddRange(officialRaw, trendRaw, discoveryRaw);
        fixture.DbContext.IntelligenceItems.AddRange(officialIntelligence, trendIntelligence, discoveryIntelligence);
        fixture.DbContext.TrendEvidence.Add(trendEvidence);
        await fixture.DbContext.SaveChangesAsync();
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
