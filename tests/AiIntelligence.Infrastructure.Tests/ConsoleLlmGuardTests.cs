using AiIntelligence.Console;
using AiIntelligence.Infrastructure.Intelligence;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class ConsoleLlmGuardTests
{
    [Fact]
    public void ApiKeyMissing_WhenRequiredCommandAndNoMock()
    {
        var options = new LlmClientOptions
        {
            ApiKey = string.Empty,
            Endpoint = "https://example.openai.azure.com/openai/v1/",
            DeploymentName = "deployment"
        };

        var missing = ConsoleLlmGuard.IsApiKeyMissingForCommand("test-llm", shouldUseMock: false, options);

        Assert.True(missing);
    }

    [Fact]
    public void ApiKeyNotRequired_WhenMockEnabled()
    {
        var options = new LlmClientOptions
        {
            ApiKey = string.Empty,
            Endpoint = "https://example.openai.azure.com/openai/v1/",
            DeploymentName = "deployment"
        };

        var missing = ConsoleLlmGuard.IsApiKeyMissingForCommand("test-llm", shouldUseMock: true, options);

        Assert.False(missing);
    }

    [Fact]
    public void ApiKeyNotRequired_WhenCommandDoesNotNeedLlm()
    {
        var options = new LlmClientOptions
        {
            ApiKey = string.Empty,
            Endpoint = "https://example.openai.azure.com/openai/v1/",
            DeploymentName = "deployment"
        };

        var missing = ConsoleLlmGuard.IsApiKeyMissingForCommand("inspect", shouldUseMock: false, options);

        Assert.False(missing);
    }
}
