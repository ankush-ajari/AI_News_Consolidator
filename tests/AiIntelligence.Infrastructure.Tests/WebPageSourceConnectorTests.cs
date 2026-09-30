using System.Net;
using AiIntelligence.Application.Content;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class WebPageSourceConnectorTests
{
    [Fact]
    public async Task FetchAsync_ParsesCandidatePosts_AndFetchesFullArticleContent()
    {
        const string listingHtml = """
        <html><body>
          <article>
            <time datetime="2026-09-20T10:00:00Z"></time>
            <a href="/ai/sample-agent-post/" class="single-click excerpt-title" data-bi-id="multisite_landing_latest_posts_grid_card">Building Agent workflows with AI SDK</a>
            <p>Agent and AI SDK excerpt.</p>
          </article>
          <article>
            <a href="/ai/unrelated/">Database indexing update</a>
            <p>Storage excerpt.</p>
          </article>
        </body></html>
        """;

        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("sample-agent-post")
                ? "<html><article><h1>Full article</h1><p>Full Microsoft AI DevBlog article content.</p></article></html>"
                : listingHtml)
        }));

        var connector = CreateConnector(handler);
        var items = await connector.FetchAsync(CreateSource("https://devblogs.microsoft.com/ai/"), CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Building Agent workflows with AI SDK", item.Title);
        Assert.Equal(new Uri("https://devblogs.microsoft.com/ai/sample-agent-post/"), item.Url);
        Assert.Contains("Agent and AI SDK excerpt", item.RawContent);
        Assert.Contains("Full Microsoft AI DevBlog article content", item.EnrichedContent);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_CreatesSingleRawSourceItem_ForStandaloneReportPageWithoutArticleCards()
    {
        const string reportHtml = """
        <html>
          <head><title>Stanford AI Index Technical Performance</title></head>
          <body>
            <main>
              <h1>Technical Performance</h1>
              <p>AI benchmark performance improved across several evaluations.</p>
              <p>Inference costs declined for selected model families.</p>
            </main>
          </body>
        </html>
        """;
        var handler = StubHttpMessageHandler.WithStringResponse(reportHtml, "text/html");
        var connector = CreateConnector(handler);
        var source = new SourceDefinition(
            Guid.NewGuid(),
            "Stanford AI Index 2026 - Technical Performance",
            "Stanford HAI",
            SourceType.WebPage,
            SourceClass.TrendResearch,
            new Uri("https://hai.stanford.edu/report/technical-performance"),
            isEnabled: true,
            new[] { "Benchmark", "Inference", "Cost" });

        var items = await connector.FetchAsync(source, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Technical Performance", item.Title);
        Assert.Equal(source.Url, item.Url);
        Assert.Equal(SourceClass.TrendResearch, item.SourceClass);
        Assert.Equal(source.Id, item.SourceDefinitionId);
        Assert.Contains("AI benchmark performance improved", item.RawContent);
        Assert.Contains("Inference costs declined", item.EnrichedContent);
        Assert.Equal(SourceClass.TrendResearch, source.SourceClass);
    }

    private static WebPageSourceConnector CreateConnector(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(WebPageSourceConnector.HttpClientName).Returns(client);

        return new WebPageSourceConnector(factory, new Sha256ContentHashService(), NullLogger<WebPageSourceConnector>.Instance);
    }

    private static SourceDefinition CreateSource(string url) => new(
        Guid.NewGuid(),
        "Microsoft AI DevBlog",
        "Microsoft",
        SourceType.WebPage,
        SourceClass.CurrentOfficial,
        new Uri(url),
        isEnabled: true,
        new[] { "Agent", "AI SDK" });
}
