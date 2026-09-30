using AiIntelligence.Application.Content;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.Persistence;
using AiIntelligence.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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

    private static SourceDefinition CreateSource() => new(
        Guid.NewGuid(),
        "Test API",
        "Vendor",
        SourceType.Api,
        SourceClass.CurrentOfficial,
        new Uri("https://example.com"),
        isEnabled: true);

    private static RawSourceItem CreateRawItem(string url, string content) => CreateRawItem(Guid.NewGuid(), url, content);

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string url, string content)
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
            new ContentHash(hashService.ComputeHash(content)));
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
