using System.Net;
using AiIntelligence.Application.Content;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class GitHubReleaseSourceConnectorTests
{
    [Fact]
    public async Task FetchAsync_MapsGitHubReleases_AndIgnoresDrafts()
    {
        const string json = """
        [
          {
            "name": "Release 1.0",
            "tag_name": "v1.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/v1.0.0",
            "published_at": "2026-09-22T10:00:00Z",
            "body": "Release notes body.",
            "draft": false,
            "prerelease": true
          },
          {
            "name": "Draft Release",
            "tag_name": "v2.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/v2.0.0",
            "published_at": "2026-09-23T10:00:00Z",
            "body": "Draft body.",
            "draft": true,
            "prerelease": false
          }
        ]
        """;

        var handler = StubHttpMessageHandler.WithStringResponse(json);
        var connector = CreateConnector(handler);

        var items = await connector.FetchAsync(CreateSource(), CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Release 1.0", item.Title);
        Assert.Equal(new Uri("https://github.com/example/repo/releases/tag/v1.0.0"), item.Url);
        Assert.Equal(DateTimeOffset.Parse("2026-09-22T10:00:00Z"), item.PublishedAt);
        Assert.Contains("Prerelease: true", item.RawContent);
        Assert.Contains("Release notes body.", item.RawContent);
        Assert.Equal("https://api.github.com/repos/example/repo/releases", handler.LastRequest?.RequestUri?.ToString());
    }

    [Fact]
    public async Task FetchAsync_DoesNotEnrich_WhenReleaseBodyIsDetailed()
    {
        var detailedBody = new string('a', 600);
        var json = "[{\"name\":\"Detailed Release\",\"tag_name\":\"v1.0.0\",\"html_url\":\"https://github.com/example/repo/releases/tag/v1.0.0\",\"published_at\":\"2026-09-22T10:00:00Z\",\"body\":\"" + detailedBody + "\",\"draft\":false,\"prerelease\":false}]";
        var handler = StubHttpMessageHandler.WithStringResponse(json);
        var connector = CreateConnector(handler);

        var item = Assert.Single(await connector.FetchAsync(CreateSource(), CancellationToken.None));

        Assert.Empty(item.EnrichedContent);
        Assert.Empty(item.ContentSourceUrls);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_EnrichesShortBody_WhenApprovedUrlExists()
    {
        const string json = """
        [
          {
            "name": "Short Release",
            "tag_name": "v1.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/v1.0.0",
            "published_at": "2026-09-22T10:00:00Z",
            "body": "See [release notes](https://learn.microsoft.com/dotnet/core/whats-new).",
            "draft": false,
            "prerelease": false
          }
        ]
        """;
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsoluteUri.Contains("api.github.com")
                ? json
                : "<html><article><h1>Release notes</h1><p>Detailed enriched release notes content.</p></article></html>")
        }));
        var connector = CreateConnector(handler);

        var item = Assert.Single(await connector.FetchAsync(CreateSource(), CancellationToken.None));

        Assert.Contains("Detailed enriched release notes content", item.EnrichedContent);
        Assert.Contains(item.ContentSourceUrls, url => url.Host == "learn.microsoft.com");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_IgnoresUnapprovedEnrichmentDomain()
    {
        const string json = """
        [
          {
            "name": "Short Release",
            "tag_name": "v1.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/v1.0.0",
            "published_at": "2026-09-22T10:00:00Z",
            "body": "See https://contoso.example/release-notes",
            "draft": false,
            "prerelease": false
          }
        ]
        """;
        var handler = StubHttpMessageHandler.WithStringResponse(json);
        var connector = CreateConnector(handler);

        var item = Assert.Single(await connector.FetchAsync(CreateSource(), CancellationToken.None));

        Assert.Empty(item.EnrichedContent);
        Assert.Empty(item.ContentSourceUrls);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_ReturnsRelease_WhenEnrichmentHttpFails()
    {
        const string json = """
        [
          {
            "name": "Short Release",
            "tag_name": "v1.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/v1.0.0",
            "published_at": "2026-09-22T10:00:00Z",
            "body": "See https://learn.microsoft.com/dotnet/core/whats-new",
            "draft": false,
            "prerelease": false
          }
        ]
        """;
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsoluteUri.Contains("api.github.com") ? HttpStatusCode.OK : HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(json)
        }));
        var connector = CreateConnector(handler);

        var item = Assert.Single(await connector.FetchAsync(CreateSource(), CancellationToken.None));

        Assert.Equal("Short Release", item.Title);
        Assert.Empty(item.EnrichedContent);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_DoesNotRecursivelyCrawlLinksFromEnrichedPage()
    {
        const string json = """
        [
          {
            "name": "Short Release",
            "tag_name": "v1.0.0",
            "html_url": "https://github.com/example/repo/releases/tag/v1.0.0",
            "published_at": "2026-09-22T10:00:00Z",
            "body": "See https://learn.microsoft.com/dotnet/core/whats-new",
            "draft": false,
            "prerelease": false
          }
        ]
        """;
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsoluteUri.Contains("api.github.com")
                ? json
                : "<html><article>Details with another link https://learn.microsoft.com/another-page</article></html>")
        }));
        var connector = CreateConnector(handler);

        var item = Assert.Single(await connector.FetchAsync(CreateSource(), CancellationToken.None));

        Assert.Contains("another link", item.EnrichedContent);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_Throws_ForHttpError()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var connector = CreateConnector(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.FetchAsync(CreateSource(), CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_RequiresConfiguredRepository()
    {
        var handler = StubHttpMessageHandler.WithStringResponse("[]");
        var connector = CreateConnector(handler, new GitHubReleaseSourceOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.FetchAsync(CreateSource(), CancellationToken.None));
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

    private static GitHubReleaseSourceConnector CreateConnector(
        HttpMessageHandler handler,
        GitHubReleaseSourceOptions? options = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GitHubReleaseSourceConnector.HttpClientName).Returns(client);

        options ??= new GitHubReleaseSourceOptions
        {
            Repositories = new Dictionary<string, GitHubRepositoryOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["example-repo"] = new GitHubRepositoryOptions
                {
                    Owner = "example",
                    Name = "repo"
                }
            }
        };

        return new GitHubReleaseSourceConnector(
            factory,
            new Sha256ContentHashService(),
            Options.Create(options),
            NullLogger<GitHubReleaseSourceConnector>.Instance);
    }

    private static SourceDefinition CreateSource() => new(
        Guid.NewGuid(),
        "example-repo",
        "GitHub",
        SourceType.GitHubRelease,
        SourceClass.CurrentOfficial,
        new Uri("https://github.com/example/repo/releases"),
        isEnabled: true);
}
