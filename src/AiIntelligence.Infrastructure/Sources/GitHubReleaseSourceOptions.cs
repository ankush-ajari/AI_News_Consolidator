namespace AiIntelligence.Infrastructure.Sources;

public sealed class GitHubReleaseSourceOptions
{
    public const string SectionName = "Sources:GitHubReleases";

    public Dictionary<string, GitHubRepositoryOptions> Repositories { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GitHubRepositoryOptions
{
    public string Owner { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}
