using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.ValueObjects;

namespace AiIntelligence.Domain.Models;

public sealed class RawSourceItem
{
    private RawSourceItem()
    {
        Title = string.Empty;
        Url = new Uri("about:blank");
        CanonicalUrl = string.Empty;
        RawContent = string.Empty;
        EnrichedContent = string.Empty;
        ContentHash = new ContentHash("placeholder");
        ContentSourceUrls = Array.Empty<Uri>();
        SourceClass = SourceClass.CurrentOfficial;
    }

    public RawSourceItem(
        Guid id,
        Guid sourceDefinitionId,
        string title,
        Uri url,
        DateTimeOffset? publishedAt,
        DateTimeOffset fetchedAt,
        string rawContent,
        ContentHash contentHash,
        string? enrichedContent = null,
        IReadOnlyCollection<Uri>? contentSourceUrls = null,
        SourceClass sourceClass = SourceClass.CurrentOfficial)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Raw source item id is required.", nameof(id));
        }

        if (sourceDefinitionId == Guid.Empty)
        {
            throw new ArgumentException("Source definition id is required.", nameof(sourceDefinitionId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Raw source item title is required.", nameof(title));
        }

        ArgumentNullException.ThrowIfNull(rawContent);

        Id = id;
        SourceDefinitionId = sourceDefinitionId;
        Title = title.Trim();
        Url = url ?? throw new ArgumentNullException(nameof(url));
        CanonicalUrl = CanonicalUrlNormalizer.Normalize(Url);
        PublishedAt = publishedAt;
        FetchedAt = fetchedAt;
        RawContent = rawContent;
        ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        EnrichedContent = enrichedContent ?? string.Empty;
        ContentSourceUrls = contentSourceUrls?.ToArray() ?? Array.Empty<Uri>();
        SourceClass = sourceClass;
    }

    public Guid Id { get; private set; }

    public Guid SourceDefinitionId { get; private set; }

    public SourceClass SourceClass { get; private set; }

    public string Title { get; private set; }

    public Uri Url { get; private set; }

    public string CanonicalUrl { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public string RawContent { get; private set; }

    public string EnrichedContent { get; private set; }

    public IReadOnlyCollection<Uri> ContentSourceUrls { get; private set; }

    public ContentHash ContentHash { get; private set; }
}
