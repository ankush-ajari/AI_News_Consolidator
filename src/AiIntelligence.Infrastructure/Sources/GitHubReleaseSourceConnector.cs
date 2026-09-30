using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiIntelligence.Application.Content;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiIntelligence.Infrastructure.Sources;

public sealed class GitHubReleaseSourceConnector : ISourceConnector
{
    public const string HttpClientName = "GitHubReleases";

    private static readonly string[] EnrichmentAllowedHosts =
    {
        "github.com",
        "learn.microsoft.com",
        "devblogs.microsoft.com",
        "dotnet.microsoft.com"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IContentHashService _contentHashService;
    private readonly IOptions<GitHubReleaseSourceOptions> _options;
    private readonly ILogger<GitHubReleaseSourceConnector> _logger;

    public GitHubReleaseSourceConnector(
        IHttpClientFactory httpClientFactory,
        IContentHashService contentHashService,
        IOptions<GitHubReleaseSourceOptions> options,
        ILogger<GitHubReleaseSourceConnector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _contentHashService = contentHashService;
        _options = options;
        _logger = logger;
    }

    public SourceType SourceType => SourceType.GitHubRelease;

    public async Task<IReadOnlyCollection<RawSourceItem>> FetchAsync(
        SourceDefinition sourceDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDefinition);
        cancellationToken.ThrowIfCancellationRequested();

        var repository = ResolveRepository(sourceDefinition);
        var requestUri = $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/releases";

        _logger.LogInformation(
            "Retrieving GitHub releases for {SourceName} from {Owner}/{Repository}.",
            sourceDefinition.Name,
            repository.Owner,
            repository.Name);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var releases = await response.Content.ReadFromJsonAsync<IReadOnlyCollection<GitHubReleaseResponse>>(
            cancellationToken: cancellationToken).ConfigureAwait(false) ?? Array.Empty<GitHubReleaseResponse>();

        var fetchedAt = DateTimeOffset.UtcNow;
        var items = new List<RawSourceItem>();
        foreach (var release in releases.Where(release => !release.Draft))
        {
            items.Add(await MapReleaseAsync(sourceDefinition, release, fetchedAt, client, cancellationToken).ConfigureAwait(false));
        }

        _logger.LogInformation(
            "Mapped {ItemCount} GitHub releases for {SourceName}.",
            items.Count,
            sourceDefinition.Name);

        return items;
    }

    private GitHubRepositoryOptions ResolveRepository(SourceDefinition sourceDefinition)
    {
        if (_options.Value.Repositories.TryGetValue(sourceDefinition.Name, out var repository)
            && !string.IsNullOrWhiteSpace(repository.Owner)
            && !string.IsNullOrWhiteSpace(repository.Name))
        {
            return repository;
        }

        throw new InvalidOperationException(
            $"GitHub repository options are required for source '{sourceDefinition.Name}'. Configure owner and name under '{GitHubReleaseSourceOptions.SectionName}:Repositories:{sourceDefinition.Name}'.");
    }

    private async Task<RawSourceItem> MapReleaseAsync(
        SourceDefinition sourceDefinition,
        GitHubReleaseResponse release,
        DateTimeOffset fetchedAt,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Untitled GitHub release";
        }

        var body = release.Body ?? string.Empty;
        var prereleasePrefix = release.Prerelease ? "Prerelease: true" : "Prerelease: false";
        var rawContent = $"{prereleasePrefix}{Environment.NewLine}{body}";
        var htmlUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? sourceDefinition.Url
            : new Uri(release.HtmlUrl);

        var enrichment = await TryEnrichAsync(title, body, client, cancellationToken).ConfigureAwait(false);

        return RawSourceItemFactory.Create(
            _contentHashService,
            sourceDefinition,
            title,
            htmlUrl,
            release.PublishedAt,
            fetchedAt,
            rawContent,
            enrichment.Content,
            enrichment.SourceUrl is null ? Array.Empty<Uri>() : new[] { htmlUrl, enrichment.SourceUrl });
    }

    private async Task<(string Content, Uri? SourceUrl)> TryEnrichAsync(
        string title,
        string body,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        if (body.Length >= 500 && !MarkdownLinkExtractor.IsPrimarilyLinks(body))
        {
            return (string.Empty, null);
        }

        var link = MarkdownLinkExtractor.ExtractLinks(body).FirstOrDefault(IsAllowedEnrichmentUrl);
        if (link is null)
        {
            return (string.Empty, null);
        }

        try
        {
            _logger.LogInformation("Enriching GitHub release {ReleaseTitle} from {EnrichmentUrl}.", title, link);
            using var response = await client.GetAsync(link, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (ReadableContentExtractor.Extract(html), link);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to enrich GitHub release {ReleaseTitle} from {EnrichmentUrl}.", title, link);
            return (string.Empty, null);
        }
    }

    private static bool IsAllowedEnrichmentUrl(Uri uri)
    {
        return uri.Scheme is "https" or "http"
            && EnrichmentAllowedHosts.Any(host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record GitHubReleaseResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}
