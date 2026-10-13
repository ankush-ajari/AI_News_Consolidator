namespace AiIntelligence.Infrastructure.Configuration;

public sealed class ReportPublishingOptions
{
    public const string SectionName = "ReportPublishing";

    public bool Enabled { get; init; }

    public string DestinationPath { get; init; } = string.Empty;

    public bool OverwriteExisting { get; init; } = true;
}
