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

public sealed class IntelligenceAnalysisRankingLlmtTests
{
    [Fact]
    public async Task AnalyzeUnprocessedAsync_LlmStillControlsRelevance()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var aiSource = await AddSourceAsync(fixture, "Microsoft AI DevBlog", SourceType.WebPage);
        fixture.DbContext.RawSourceItems.Add(CreateRawItem(aiSource.Id, "AI agent evaluation updates", "New agent evaluation workflow for MCP."));
        await fixture.DbContext.SaveChangesAsync();

        var service = new IntelligenceAnalysisService(
            new RawSourceRepository(fixture.DbContext),
            new SourceDefinitionRepository(fixture.DbContext),
            new IntelligenceRepository(fixture.DbContext),
            new StubExtractor(false),
            NullLogger<IntelligenceAnalysisService>.Instance);

        var result = await service.AnalyzeUnprocessedAsync(CancellationToken.None, includeDiagnostics: true);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(0, result.PersistedCount);
        Assert.Equal(1, result.IrrelevantCount);
    }

    private static async Task<SourceDefinition> AddSourceAsync(SqliteFixture fixture, string name, SourceType sourceType)
    {
        var source = new SourceDefinition(Guid.NewGuid(), name, "Vendor", sourceType, SourceClass.CurrentOfficial, new Uri("https://example.com"), true);
        fixture.DbContext.SourceDefinitions.Add(source);
        await fixture.DbContext.SaveChangesAsync();
        return source;
    }

    private static RawSourceItem CreateRawItem(Guid sourceDefinitionId, string title, string content)
    {
        return new RawSourceItem(
            Guid.NewGuid(),
            sourceDefinitionId,
            title,
            new Uri("https://example.com/item"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            content,
            new ContentHash(Guid.NewGuid().ToString("N")));
    }

    private sealed class StubExtractor : IIntelligenceExtractor
    {
        private readonly bool _isRelevant;

        public StubExtractor(bool isRelevant)
        {
            _isRelevant = isRelevant;
        }

        public Task<IntelligenceExtractionResult> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
        {
            return Task.FromResult(new IntelligenceExtractionResult(
                _isRelevant,
                "Vendor",
                "Topic",
                "Category",
                "Framework",
                "Summary",
                Array.Empty<string>(),
                Array.Empty<string>(),
                "Unknown",
                0.5m,
                Array.Empty<string>(),
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
