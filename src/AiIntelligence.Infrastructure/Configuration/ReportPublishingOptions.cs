namespace AiIntelligence.Infrastructure.Configuration;

public sealed class ReportPublishingOptions
{
    public const string SectionName = "ReportPublishing";

    public bool Enabled { get; init; }

    public string DestinationPath { get; init; } = string.Empty;

    public bool OverwriteExisting { get; init; } = true;

    // Optional prefix used when generating destination filenames.
    // Defaults to "AI-Technology-Intelligence". This value is sanitized to
    // avoid invalid filename characters or path traversal.
    public string FileNamePrefix { get; init; } = "AI-Technology-Intelligence";
}
