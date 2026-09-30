namespace AiIntelligence.Infrastructure.Intelligence;

public sealed class LlmClientOptions
{
    public const string SectionName = "Foundry";

    public string Endpoint { get; init; } = string.Empty;

    public string DeploymentName { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;
}
