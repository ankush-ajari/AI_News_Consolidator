using System.ServiceModel.Syndication;
using System.Xml;
using AiIntelligence.Application.Content;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Infrastructure.Sources;

public sealed class RssSourceConnector : ISourceConnector
{
    public const string HttpClientName = "RssSources";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IContentHashService _contentHashService;
    private readonly ILogger<RssSourceConnector> _logger;

    public RssSourceConnector(
        IHttpClientFactory httpClientFactory,
        IContentHashService contentHashService,
        ILogger<RssSourceConnector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _contentHashService = contentHashService;
        _logger = logger;
    }

    public SourceType SourceType => SourceType.Rss;

    public async Task<IReadOnlyCollection<RawSourceItem>> FetchAsync(
        SourceDefinition sourceDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Downloading RSS feed for {SourceName} from {SourceUrl}.", sourceDefinition.Name, sourceDefinition.Url);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(sourceDefinition.Url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });

        var feed = SyndicationFeed.Load(reader)
            ?? throw new InvalidOperationException($"RSS feed '{sourceDefinition.Name}' could not be parsed.");

        var fetchedAt = DateTimeOffset.UtcNow;
        var items = feed.Items.Select(item => MapItem(sourceDefinition, item, fetchedAt, _contentHashService)).ToArray();

        _logger.LogInformation("Parsed {ItemCount} RSS items for {SourceName}.", items.Length, sourceDefinition.Name);

        return items;
    }

    private static RawSourceItem MapItem(
        SourceDefinition sourceDefinition,
        SyndicationItem item,
        DateTimeOffset fetchedAt,
        IContentHashService contentHashService)
    {
        var title = item.Title?.Text;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Untitled RSS item";
        }

        var url = item.Links.FirstOrDefault()?.Uri ?? sourceDefinition.Url;
        DateTimeOffset? publishedAt = item.PublishDate == DateTimeOffset.MinValue ? null : item.PublishDate;
        var content = item.Content switch
        {
            TextSyndicationContent textContent => textContent.Text,
            _ => item.Summary?.Text ?? string.Empty
        };

        return RawSourceItemFactory.Create(
            contentHashService,
            sourceDefinition,
            title,
            url,
            publishedAt,
            fetchedAt,
            content);
    }
}
