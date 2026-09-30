using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Domain.Models;

public sealed class IntelligenceItem
{
    private IntelligenceItem()
    {
        Vendor = string.Empty;
        Topic = string.Empty;
        Category = string.Empty;
        ProductOrFramework = string.Empty;
        Summary = string.Empty;
        Capabilities = Array.Empty<string>();
        Limitations = Array.Empty<string>();
        ReleaseStage = string.Empty;
        SourceUrl = new Uri("about:blank");
    }

    public IntelligenceItem(
        Guid id,
        Guid sourceItemId,
        string vendor,
        string topic,
        string category,
        string productOrFramework,
        string summary,
        IReadOnlyCollection<string> capabilities,
        IReadOnlyCollection<string> limitations,
        string releaseStage,
        SourceClass sourceClass,
        DateTimeOffset? publishedAt,
        Uri sourceUrl)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Intelligence item id is required.", nameof(id));
        }

        if (sourceItemId == Guid.Empty)
        {
            throw new ArgumentException("Source item id is required.", nameof(sourceItemId));
        }

        if (string.IsNullOrWhiteSpace(vendor))
        {
            throw new ArgumentException("Vendor is required.", nameof(vendor));
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Summary is required.", nameof(summary));
        }

        Id = id;
        SourceItemId = sourceItemId;
        Vendor = vendor.Trim();
        Topic = topic.Trim();
        Category = category.TrimOrEmpty();
        ProductOrFramework = productOrFramework.TrimOrEmpty();
        Summary = summary.Trim();
        Capabilities = capabilities?.ToArray() ?? Array.Empty<string>();
        Limitations = limitations?.ToArray() ?? Array.Empty<string>();
        ReleaseStage = releaseStage.TrimOrEmpty();
        SourceClass = sourceClass;
        PublishedAt = publishedAt;
        SourceUrl = sourceUrl ?? throw new ArgumentNullException(nameof(sourceUrl));
    }

    public Guid Id { get; private set; }

    public Guid SourceItemId { get; private set; }

    public string Vendor { get; private set; }

    public string Topic { get; private set; }

    public string Category { get; private set; }

    public string ProductOrFramework { get; private set; }

    public string Summary { get; private set; }

    public IReadOnlyCollection<string> Capabilities { get; private set; }

    public IReadOnlyCollection<string> Limitations { get; private set; }

    public string ReleaseStage { get; private set; }

    public SourceClass SourceClass { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public Uri SourceUrl { get; private set; }
}
