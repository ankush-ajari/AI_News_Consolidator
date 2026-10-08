using AiIntelligence.Application.Content;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.Persistence;
using AiIntelligence.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Data.Common;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Repository_InsertsAndRetrievesRawSourceItem()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = new RawSourceRepository(fixture.DbContext);
        var source = CreateSource();
        await AddSourceAsync(fixture, source);
        var item = CreateRawItem(source.Id, "https://example.com/item", "content-v1");

        await repository.AddAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var items = await repository.ListAsync(CancellationToken.None);
        var saved = Assert.Single(items);
        Assert.Equal(item.Title, saved.Title);
        Assert.Equal(item.ContentHash, saved.ContentHash);
    }

    [Fact]
    public async Task Repository_DetectsIdenticalContent_ByCanonicalUrlAndHash()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = new RawSourceRepository(fixture.DbContext);
        var source = CreateSource();
        await AddSourceAsync(fixture, source);
        var item = CreateRawItem(source.Id, "https://example.com/item#section", "content-v1");

        await repository.AddAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var duplicate = await repository.FindByCanonicalUrlAndHashAsync(
            CanonicalUrlNormalizer.Normalize(new Uri("https://EXAMPLE.com/item")),
            item.ContentHash.Value,
            CancellationToken.None);

        Assert.NotNull(duplicate);
    }

    [Fact]
    public async Task Repository_AllowsChangedContent_ForSameCanonicalUrl()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var repository = new RawSourceRepository(fixture.DbContext);
        var source = CreateSource();
        await AddSourceAsync(fixture, source);
        var first = CreateRawItem(source.Id, "https://example.com/item", "content-v1");
        var second = CreateRawItem(source.Id, "https://example.com/item", "content-v2 changed significantly");

        await repository.AddAsync(first, CancellationToken.None);
        await repository.AddAsync(second, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var items = await repository.ListAsync(CancellationToken.None);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetByIdsAsync_FiltersAndProjectsWithoutLargeContentColumns()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource();
        await AddSourceAsync(fixture, source);
        var required = CreateRawItem(source.Id, "https://example.com/required", new string('r', 20_000));
        var unrelated = CreateRawItem(source.Id, "https://example.com/unrelated", new string('u', 20_000));
        fixture.DbContext.RawSourceItems.AddRange(required, unrelated);
        await fixture.DbContext.SaveChangesAsync();

        var summaries = await new RawSourceRepository(fixture.DbContext)
            .GetByIdsAsync(new[] { required.Id }, CancellationToken.None);

        Assert.Equal(required.Id, Assert.Single(summaries).Id);
        Assert.Contains("WHERE", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RawContent", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EnrichedContent", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListForCorrelationAsync_FiltersOrdersAndLimitsInSql()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource();
        await AddSourceAsync(fixture, source);
        var rawItems = new[]
        {
            CreateRawItem(source.Id, "https://example.com/a", "raw-a"),
            CreateRawItem(source.Id, "https://example.com/b", "raw-b"),
            CreateRawItem(source.Id, "https://example.com/c", "raw-c")
        };
        fixture.DbContext.RawSourceItems.AddRange(rawItems);
        fixture.DbContext.IntelligenceItems.AddRange(rawItems.Select((raw, index) => CreateIntelligence(raw.Id, index)));
        await fixture.DbContext.SaveChangesAsync();

        var rows = await new IntelligenceRepository(fixture.DbContext)
            .ListForCorrelationAsync(rawItems.Select(item => item.Id).ToArray(), 2, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Contains("WHERE", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListCandidatesForCorrelationAsync_FiltersResearchSourcesAndOmitsUnusedPayload()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var trendSource = CreateSource(SourceClass.TrendResearch, "Trend source");
        var currentSource = CreateSource(SourceClass.CurrentOfficial, "Current source");
        await AddSourceAsync(fixture, trendSource);
        await AddSourceAsync(fixture, currentSource);
        var trendRaw = CreateRawItem(trendSource.Id, "https://example.com/trend", "trend raw", SourceClass.TrendResearch);
        var currentRaw = CreateRawItem(currentSource.Id, "https://example.com/current", "current raw");
        fixture.DbContext.RawSourceItems.AddRange(trendRaw, currentRaw);
        fixture.DbContext.TrendEvidence.AddRange(CreateTrend(trendRaw.Id), CreateTrend(currentRaw.Id));
        await fixture.DbContext.SaveChangesAsync();

        var repository = new TrendEvidenceRepository(fixture.DbContext);
        var candidates = new List<TrendEvidenceCandidate>();
        await foreach (var candidate in repository.StreamCandidatesForCorrelationAsync(CancellationToken.None))
        {
            candidates.Add(candidate);
        }

        Assert.Equal(trendRaw.Id, Assert.Single(candidates).SourceItemId);
        Assert.Contains("WHERE", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RawSourceItems", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QuantitativeEvidence", fixture.CommandInterceptor.LastCommandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestAsync_DeduplicatesUnchangedRecords()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var source = CreateSource();
        var item = CreateRawItem(source.Id, "https://example.com/item", "content-v1");
        var connector = Substitute.For<ISourceConnector>();
        connector.SourceType.Returns(SourceType.Api);
        connector.FetchAsync(source, Arg.Any<CancellationToken>()).Returns(new[] { item });
        var factory = new SourceConnectorFactory(new[] { connector });
        var repository = new RawSourceRepository(fixture.DbContext);
        var sourceRepository = new SourceDefinitionRepository(fixture.DbContext);
        var service = new SourceIngestionService(factory, repository, sourceRepository, NullLogger<SourceIngestionService>.Instance);

        var firstRun = await service.IngestAsync(new[] { source }, CancellationToken.None);
        var secondRun = await service.IngestAsync(new[] { source }, CancellationToken.None);

        Assert.Equal(1, firstRun.InsertedCount);
        Assert.Equal(0, firstRun.DuplicateCount);
        Assert.Equal(0, secondRun.InsertedCount);
        Assert.Equal(1, secondRun.DuplicateCount);
        Assert.Single(await repository.ListAsync(CancellationToken.None));
    }

    private static async Task AddSourceAsync(SqliteFixture fixture, SourceDefinition source)
    {
        fixture.DbContext.SourceDefinitions.Add(source);
        await fixture.DbContext.SaveChangesAsync();
    }

    private static SourceDefinition CreateSource(SourceClass sourceClass = SourceClass.CurrentOfficial, string name = "Test API") => new(
        Guid.NewGuid(),
        name,
        "Vendor",
        SourceType.Api,
        sourceClass,
        new Uri("https://example.com"),
        isEnabled: true);

    private static RawSourceItem CreateRawItem(string url, string content) => CreateRawItem(Guid.NewGuid(), url, content);

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string url, string content, SourceClass sourceClass = SourceClass.CurrentOfficial)
    {
        var hashService = new Sha256ContentHashService();
        return new RawSourceItem(
            Guid.NewGuid(),
            sourceDefinitionId,
            "Title",
            new Uri(url),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            content,
            new ContentHash(hashService.ComputeHash(content)),
            sourceClass: sourceClass);
    }

    private static IntelligenceItem CreateIntelligence(Guid sourceItemId, int index) => new(
        Guid.NewGuid(),
        sourceItemId,
        "Vendor",
        $"Topic {index}",
        "Category",
        "Product",
        $"Summary {index}",
        Array.Empty<string>(),
        Array.Empty<string>(),
        "GA",
        SourceClass.CurrentOfficial,
        DateTimeOffset.UtcNow,
        new Uri($"https://example.com/item-{index}"));

    private static TrendEvidence CreateTrend(Guid sourceItemId) => new(
        Guid.NewGuid(),
        sourceItemId,
        "Trend topic",
        "2026",
        TrendEvidencePeriodProvenance.SourceContent,
        "Trend finding",
        "Evidence summary",
        0.8m,
        new Uri("https://example.com/trend"),
        quantitativeEvidence: new string('q', 10_000));

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(SqliteConnection connection, AiIntelligenceDbContext dbContext, CommandCaptureInterceptor commandInterceptor)
        {
            Connection = connection;
            DbContext = dbContext;
            CommandInterceptor = commandInterceptor;
        }

        public SqliteConnection Connection { get; }

        public AiIntelligenceDbContext DbContext { get; }

        public CommandCaptureInterceptor CommandInterceptor { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var commandInterceptor = new CommandCaptureInterceptor();
            var options = new DbContextOptionsBuilder<AiIntelligenceDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(commandInterceptor)
                .Options;
            var dbContext = new AiIntelligenceDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, dbContext, commandInterceptor);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public string LastCommandText { get; private set; } = string.Empty;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            LastCommandText = command.CommandText;
            return ValueTask.FromResult(result);
        }
    }
}
