using System.Text.RegularExpressions;
using AiIntelligence.Application.Content;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Infrastructure.Sources;

public sealed partial class WebPageSourceConnector : ISourceConnector
{
    public const string HttpClientName = "WebPageSources";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IContentHashService _contentHashService;
    private readonly ILogger<WebPageSourceConnector> _logger;

    public WebPageSourceConnector(
        IHttpClientFactory httpClientFactory,
        IContentHashService contentHashService,
        ILogger<WebPageSourceConnector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _contentHashService = contentHashService;
        _logger = logger;
    }

    public SourceType SourceType => SourceType.WebPage;

    public async Task<IReadOnlyCollection<RawSourceItem>> FetchAsync(
        SourceDefinition sourceDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        cancellationToken.ThrowIfCancellationRequested();

        var client = _httpClientFactory.CreateClient(HttpClientName);
        _logger.LogInformation("Downloading webpage source {SourceName} from {SourceUrl}.", sourceDefinition.Name, sourceDefinition.Url);

        using var response = await client.GetAsync(sourceDefinition.Url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var candidatePosts = ExtractCandidatePosts(html, sourceDefinition.Url)
            .Where(post => MatchesHints(post, sourceDefinition.IncludeTopicHints))
            .Take(5)
            .ToArray();

        var fetchedAt = DateTimeOffset.UtcNow;
        var items = new List<RawSourceItem>();
        foreach (var post in candidatePosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enrichedContent = await TryFetchArticleAsync(client, post.Url, cancellationToken).ConfigureAwait(false);
            var rawContent = string.Join(" ", new[] { post.Title, post.Excerpt }.Where(value => !string.IsNullOrWhiteSpace(value)));

            items.Add(RawSourceItemFactory.Create(
                _contentHashService,
                sourceDefinition,
                post.Title,
                post.Url,
                post.PublishedAt,
                fetchedAt,
                rawContent,
                enrichedContent,
                new[] { post.Url }));
        }

        if (items.Count == 0)
        {
            var readableContent = ReadableContentExtractor.Extract(html);
            var fallbackContent = !string.IsNullOrWhiteSpace(readableContent)
                ? readableContent
                : ReadableContentExtractor.Extract($"<body>{html}</body>");

            if (string.IsNullOrWhiteSpace(fallbackContent) && !string.IsNullOrWhiteSpace(html))
            {
                fallbackContent = html;
            }

            if (!string.IsNullOrWhiteSpace(fallbackContent))
            {
                var title = ExtractPageTitle(html) ?? sourceDefinition.Name;
                items.Add(RawSourceItemFactory.Create(
                    _contentHashService,
                    sourceDefinition,
                    title,
                    sourceDefinition.Url,
                    publishedAt: null,
                    fetchedAt,
                    fallbackContent,
                    fallbackContent,
                    new[] { sourceDefinition.Url }));
            }
        }

        _logger.LogInformation(
            "Mapped {ItemCount} webpage items for {SourceName}. SkippedCandidateCount: {SkippedCandidateCount}.",
            items.Count,
            sourceDefinition.Name,
            Math.Max(0, candidatePosts.Length - items.Count));
        return items;
    }

    private async Task<string> TryFetchArticleAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ReadableContentExtractor.Extract(html);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to fetch full article content from {ArticleUrl}.", url);
            return string.Empty;
        }
    }

    private static string? ExtractPageTitle(string html)
    {
        var h1Match = H1Regex().Match(html);
        if (h1Match.Success)
        {
            return ReadableContentExtractor.Extract(h1Match.Groups[1].Value);
        }

        var titleMatch = TitleRegex().Match(html);
        return titleMatch.Success ? ReadableContentExtractor.Extract(titleMatch.Groups[1].Value) : null;
    }

    private static IReadOnlyCollection<WebPageCandidatePost> ExtractCandidatePosts(string html, Uri baseUri)
    {
        var posts = new List<WebPageCandidatePost>();
        foreach (Match linkMatch in AnchorRegex().Matches(html))
        {
            if (!IsPostTitleAnchor(linkMatch.Value))
            {
                continue;
            }

            var href = linkMatch.Groups[1].Value;
            var title = ReadableContentExtractor.Extract(linkMatch.Groups[2].Value);
            if (string.IsNullOrWhiteSpace(title) || !Uri.TryCreate(baseUri, href, out var url))
            {
                continue;
            }

            if (!IsLikelyArticleUrl(baseUri, url))
            {
                continue;
            }

            var cardHtml = GetForwardHtml(html, linkMatch.Index, 1200);
            var dateContext = GetSurroundingHtml(html, linkMatch.Index, 1200);
            var excerpt = ExtractMetaAroundAnchor(cardHtml);
            var publishedAt = ExtractDate(dateContext);

            posts.Add(new WebPageCandidatePost(title, url, publishedAt, excerpt));
        }

        return posts
            .GroupBy(post => post.Url.AbsoluteUri)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool MatchesHints(WebPageCandidatePost post, IReadOnlyCollection<string> hints)
    {
        if (hints.Count == 0)
        {
            return true;
        }

        var searchable = $"{post.Title} {post.Excerpt}";
        return hints.Any(hint => searchable.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPostTitleAnchor(string anchorHtml)
    {
        return anchorHtml.Contains("excerpt-title", StringComparison.OrdinalIgnoreCase)
            || anchorHtml.Contains("multisite_landing_latest_posts_grid_card", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyArticleUrl(Uri baseUri, Uri candidateUrl)
    {
        if (!string.Equals(candidateUrl.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidateUrl.Segments.Length >= 3
            && !candidateUrl.Fragment.Contains("mainContent", StringComparison.OrdinalIgnoreCase)
            && !candidateUrl.AbsolutePath.Contains("/page/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetForwardHtml(string html, int index, int length)
    {
        return html.Substring(index, Math.Min(html.Length - index, length));
    }

    private static string GetSurroundingHtml(string html, int index, int radius)
    {
        var start = Math.Max(0, index - radius);
        var length = Math.Min(html.Length - start, radius * 2);
        return html.Substring(start, length);
    }

    private static string ExtractMetaAroundAnchor(string html)
    {
        var paragraphMatch = ParagraphRegex().Match(html);
        if (paragraphMatch.Success)
        {
            return ReadableContentExtractor.Extract(paragraphMatch.Groups[1].Value);
        }

        return ReadableContentExtractor.Extract(html);
    }

    private static DateTimeOffset? ExtractDate(string html)
    {
        var timeMatch = TimeRegex().Match(html);
        if (timeMatch.Success && DateTimeOffset.TryParse(timeMatch.Groups[1].Value, out var parsedTime))
        {
            return parsedTime;
        }

        var dateMatch = DateRegex().Match(html);
        if (dateMatch.Success && DateTimeOffset.TryParse(dateMatch.Value, out var parsedDate))
        {
            return parsedDate;
        }

        var monthDateMatch = MonthDateRegex().Match(html);
        if (monthDateMatch.Success && DateTimeOffset.TryParse(monthDateMatch.Value, out var parsedMonthDate))
        {
            return parsedMonthDate;
        }

        return null;
    }

    [GeneratedRegex("<[^>]*class=[\"'][^\"']*post-card[^\"']*[\"'][^>]*>.*?</div>\\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PostCardRegex();

    [GeneratedRegex("<a[^>]+href=[\"']([^\"']+)[\"'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H1Regex();

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex("<time[^>]+datetime=[\"']([^\"']+)[\"'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{1,2},\s+\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDateRegex();

    private sealed record WebPageCandidatePost(
        string Title,
        Uri Url,
        DateTimeOffset? PublishedAt,
        string Excerpt);
}
