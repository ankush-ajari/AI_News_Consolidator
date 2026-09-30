using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Domain.Models;

public sealed class SourceDefinition
{
    private SourceDefinition()
    {
        Name = string.Empty;
        Vendor = string.Empty;
        Url = new Uri("about:blank");
        IncludeTopicHints = Array.Empty<string>();
    }

    public SourceDefinition(
        Guid id,
        string name,
        string vendor,
        SourceType sourceType,
        SourceClass sourceClass,
        Uri url,
        bool isEnabled,
        IReadOnlyCollection<string>? includeTopicHints = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Source definition id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Source name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(vendor))
        {
            throw new ArgumentException("Source vendor is required.", nameof(vendor));
        }

        Id = id;
        Name = name.Trim();
        Vendor = vendor.Trim();
        SourceType = sourceType;
        SourceClass = sourceClass;
        Url = url ?? throw new ArgumentNullException(nameof(url));
        IsEnabled = isEnabled;
        IncludeTopicHints = includeTopicHints?
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Select(hint => hint.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Vendor { get; private set; }

    public SourceType SourceType { get; private set; }

    public SourceClass SourceClass { get; private set; }

    public Uri Url { get; private set; }

    public bool IsEnabled { get; private set; }

    public IReadOnlyCollection<string> IncludeTopicHints { get; private set; }
}
