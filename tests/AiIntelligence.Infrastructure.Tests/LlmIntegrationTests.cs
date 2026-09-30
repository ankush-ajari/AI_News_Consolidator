using AiIntelligence.Application.Intelligence;
using AiIntelligence.Infrastructure.Intelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class LlmIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CompleteAsync_CanCallRealLlm_WhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("ENABLE_AI_INTEGRATION_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("Foundry__Endpoint") ?? throw new InvalidOperationException("Foundry__Endpoint is required.");
        var deploymentName = Environment.GetEnvironmentVariable("Foundry__DeploymentName") ?? throw new InvalidOperationException("Foundry__DeploymentName is required.");
        var apiKey = Environment.GetEnvironmentVariable("Foundry__ApiKey") ?? throw new InvalidOperationException("Foundry__ApiKey is required.");

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpJsonLlmClient.HttpClientName).Returns(new HttpClient());
        var client = new HttpJsonLlmClient(
            factory,
            Options.Create(new LlmClientOptions { Endpoint = endpoint, DeploymentName = deploymentName, ApiKey = apiKey }),
            NullLogger<HttpJsonLlmClient>.Instance);

        var response = await client.CompleteAsync(
            new LLMRequest("Return exactly the requested text.", "Return exactly: MODEL_OK", null, null),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Content));
    }
}
