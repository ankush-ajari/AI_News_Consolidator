using System.Net;
using AiIntelligence.Application.Content;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class RssSourceConnectorTests
{
    [Fact]
    public async Task FetchAsync_Parses_StaticRssXml()
    {
        const string rss = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <title>AI Feed</title>
            <item>
              <title>New AI Capability</title>
              <link>https://example.com/new-ai-capability</link>
              <pubDate>Tue, 22 Sep 2026 10:00:00 GMT</pubDate>
              <description>Capability description.</description>
            </item>
          </channel>
        </rss>
        """;

        var handler = StubHttpMessageHandler.WithStringResponse(rss, "application/rss+xml");
        var connector = CreateConnector(handler);
        var source = CreateSource();

        var items = await connector.FetchAsync(source, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("New AI Capability", item.Title);
        Assert.Equal(new Uri("https://example.com/new-ai-capability"), item.Url);
        Assert.Equal("Capability description.", item.RawContent);
        Assert.NotNull(item.PublishedAt);
    }

    [Fact]
    public async Task FetchAsync_Throws_ForHttpError()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var connector = CreateConnector(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.FetchAsync(CreateSource(), CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_Throws_ForMalformedRss()
    {
        var handler = StubHttpMessageHandler.WithStringResponse("<rss><channel><item>", "application/rss+xml");
        var connector = CreateConnector(handler);

        await Assert.ThrowsAnyAsync<Exception>(() => connector.FetchAsync(CreateSource(), CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_PropagatesCancellation()
    {
        var handler = new StubHttpMessageHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var connector = CreateConnector(handler);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => connector.FetchAsync(CreateSource(), cancellationTokenSource.Token));
    }

    private static RssSourceConnector CreateConnector(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RssSourceConnector.HttpClientName).Returns(client);

        return new RssSourceConnector(factory, new Sha256ContentHashService(), NullLogger<RssSourceConnector>.Instance);
    }

    private static SourceDefinition CreateSource() => new(
        Guid.NewGuid(),
        "Test RSS",
        "Vendor",
        SourceType.Rss,
        SourceClass.CurrentOfficial,
        new Uri("https://example.com/feed"),
        isEnabled: true);
}
