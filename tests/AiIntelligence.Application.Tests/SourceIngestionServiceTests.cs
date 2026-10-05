using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Application.Tests;

public sealed class SourceIngestionServiceTests
{
    [Fact]
    public async Task FetchAsync_Continues_WhenOneSourceFails()
    {
        var failedSource = CreateSource("Failed", SourceType.Rss);
        var successfulSource = CreateSource("Successful", SourceType.GitHubRelease);
        var expectedItem = CreateRawSourceItem(successfulSource);

        var failingConnector = Substitute.For<ISourceConnector>();
        failingConnector.SourceType.Returns(SourceType.Rss);
        failingConnector.FetchAsync(failedSource, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyCollection<RawSourceItem>>>(_ => throw new HttpRequestException("RSS failed"));

        var successfulConnector = Substitute.For<ISourceConnector>();
        successfulConnector.SourceType.Returns(SourceType.GitHubRelease);
        successfulConnector.FetchAsync(successfulSource, Arg.Any<CancellationToken>())
            .Returns(new[] { expectedItem });

        var factory = new SourceConnectorFactory(new[] { failingConnector, successfulConnector });
        var service = new SourceIngestionService(factory, NullLogger<SourceIngestionService>.Instance);

        var result = await service.FetchAsync(new[] { failedSource, successfulSource }, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Same(expectedItem, result.Items.Single());
        Assert.Single(result.Failures);
        Assert.Equal(failedSource, result.Failures.Single().Source);
    }

    [Fact]
    public async Task IngestAsync_FetchesAndPersistsAllEnabledSourceClasses()
    {
        var official = CreateSource("Official Web", SourceType.WebPage, SourceClass.CurrentOfficial);
        var trend = CreateSource("Trend Web", SourceType.WebPage, SourceClass.TrendResearch);
        var discovery = CreateSource("Discovery RSS", SourceType.Rss, SourceClass.ResearchDiscovery);
        var webConnector = Substitute.For<ISourceConnector>();
        webConnector.SourceType.Returns(SourceType.WebPage);
        webConnector.FetchAsync(official, Arg.Any<CancellationToken>()).Returns(new[] { CreateRawSourceItem(official) });
        webConnector.FetchAsync(trend, Arg.Any<CancellationToken>()).Returns(new[] { CreateRawSourceItem(trend) });
        var rssConnector = Substitute.For<ISourceConnector>();
        rssConnector.SourceType.Returns(SourceType.Rss);
        rssConnector.FetchAsync(discovery, Arg.Any<CancellationToken>()).Returns(new[] { CreateRawSourceItem(discovery) });
        var rawRepository = Substitute.For<IRawSourceRepository>();
        rawRepository.FindBySourceDefinitionAndCanonicalUrlAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RawSourceItem?)null);
        rawRepository.FindByCanonicalUrlAndHashAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RawSourceItem?)null);
        rawRepository.UpdateAsync(Arg.Any<RawSourceItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sourceRepository = Substitute.For<ISourceDefinitionRepository>();
        var service = new SourceIngestionService(
            new SourceConnectorFactory(new[] { webConnector, rssConnector }),
            rawRepository,
            sourceRepository,
            NullLogger<SourceIngestionService>.Instance);

        var result = await service.IngestAsync(new[] { official, trend, discovery }, CancellationToken.None);

        Assert.Equal(3, result.FetchedCount);
        Assert.Equal(3, result.InsertedCount);
        await webConnector.Received(1).FetchAsync(official, Arg.Any<CancellationToken>());
        await webConnector.Received(1).FetchAsync(trend, Arg.Any<CancellationToken>());
        await rssConnector.Received(1).FetchAsync(discovery, Arg.Any<CancellationToken>());
        await rawRepository.Received(1).AddAsync(Arg.Is<RawSourceItem>(item => item.SourceClass == SourceClass.CurrentOfficial), Arg.Any<CancellationToken>());
        await rawRepository.Received(1).AddAsync(Arg.Is<RawSourceItem>(item => item.SourceClass == SourceClass.TrendResearch), Arg.Any<CancellationToken>());
        await rawRepository.Received(1).AddAsync(Arg.Is<RawSourceItem>(item => item.SourceClass == SourceClass.ResearchDiscovery), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_DoesNotFilterOutTrendResearch()
    {
        var trend = CreateSource("Trend Web", SourceType.WebPage, SourceClass.TrendResearch);
        var webConnector = Substitute.For<ISourceConnector>();
        webConnector.SourceType.Returns(SourceType.WebPage);
        webConnector.FetchAsync(trend, Arg.Any<CancellationToken>()).Returns(new[] { CreateRawSourceItem(trend) });
        var rawRepository = Substitute.For<IRawSourceRepository>();
        rawRepository.FindBySourceDefinitionAndCanonicalUrlAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RawSourceItem?)null);
        rawRepository.FindByCanonicalUrlAndHashAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RawSourceItem?)null);
        rawRepository.UpdateAsync(Arg.Any<RawSourceItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var service = new SourceIngestionService(
            new SourceConnectorFactory(new[] { webConnector }),
            rawRepository,
            Substitute.For<ISourceDefinitionRepository>(),
            NullLogger<SourceIngestionService>.Instance);

        var result = await service.IngestAsync(new[] { trend }, CancellationToken.None);

        Assert.Equal(1, result.FetchedCount);
        Assert.Equal(1, result.InsertedCount);
        await webConnector.Received(1).FetchAsync(trend, Arg.Any<CancellationToken>());
        await rawRepository.Received(1).AddAsync(Arg.Is<RawSourceItem>(item => item.SourceClass == SourceClass.TrendResearch && item.SourceDefinitionId == trend.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_TreatsSameUrlAndHashAcrossDifferentSourcesAsDuplicate()
    {
        var first = new SourceDefinition(Guid.NewGuid(), "First", "Vendor", SourceType.WebPage, SourceClass.CurrentOfficial, new Uri("https://example.com/shared"), true);
        var second = new SourceDefinition(Guid.NewGuid(), "Second", "Vendor", SourceType.WebPage, SourceClass.TrendResearch, new Uri("https://example.com/shared"), true);
        var firstItem = CreateRawSourceItem(first);
        var secondItem = new RawSourceItem(
            Guid.NewGuid(),
            second.Id,
            "Title",
            firstItem.Url,
            firstItem.PublishedAt,
            DateTimeOffset.UtcNow,
            firstItem.RawContent,
            firstItem.ContentHash,
            sourceClass: second.SourceClass);
        var connector = Substitute.For<ISourceConnector>();
        connector.SourceType.Returns(SourceType.WebPage);
        connector.FetchAsync(first, Arg.Any<CancellationToken>()).Returns(new[] { firstItem });
        connector.FetchAsync(second, Arg.Any<CancellationToken>()).Returns(new[] { secondItem });
        var rawRepository = Substitute.For<IRawSourceRepository>();
        rawRepository.FindBySourceDefinitionAndCanonicalUrlAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RawSourceItem?)null, (RawSourceItem?)null);
        rawRepository.FindByCanonicalUrlAndHashAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RawSourceItem?)null, firstItem);
        rawRepository.UpdateAsync(Arg.Any<RawSourceItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var service = new SourceIngestionService(
            new SourceConnectorFactory(new[] { connector }),
            rawRepository,
            Substitute.For<ISourceDefinitionRepository>(),
            NullLogger<SourceIngestionService>.Instance);

        var result = await service.IngestAsync(new[] { first, second }, CancellationToken.None);

        Assert.Equal(2, result.FetchedCount);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.DuplicateCount);
        await rawRepository.Received(1).AddAsync(Arg.Any<RawSourceItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_PropagatesCancellation()
    {
        var source = CreateSource("RSS", SourceType.Rss);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var connector = Substitute.For<ISourceConnector>();
        connector.SourceType.Returns(SourceType.Rss);

        var factory = new SourceConnectorFactory(new[] { connector });
        var service = new SourceIngestionService(factory, NullLogger<SourceIngestionService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.FetchAsync(new[] { source }, cancellationTokenSource.Token));

        await connector.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    private static SourceDefinition CreateSource(string name, SourceType sourceType) => CreateSource(name, sourceType, SourceClass.CurrentOfficial);

    private static SourceDefinition CreateSource(string name, SourceType sourceType, SourceClass sourceClass) => new(
        Guid.NewGuid(),
        name,
        "Vendor",
        sourceType,
        sourceClass,
        new Uri($"https://example.com/{Uri.EscapeDataString(name)}"),
        isEnabled: true);

    private static RawSourceItem CreateRawSourceItem(SourceDefinition sourceDefinition) => new(
        Guid.NewGuid(),
        sourceDefinition.Id,
        "Title",
        sourceDefinition.Url,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "Content",
        new ContentHash("hash"),
        sourceClass: sourceDefinition.SourceClass);
}
