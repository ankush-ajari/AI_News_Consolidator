using AiIntelligence.Infrastructure.Intelligence;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class AgentFrameworkFoundryOptionsTests
{
    [Fact]
    public void AgentFramework_UsesSameFoundryOptionsBinding()
    {
        Environment.SetEnvironmentVariable("Foundry__ApiKey", "env-secret");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Foundry:Endpoint"] = "https://example.openai.azure.com/openai/v1/",
                    ["Foundry:DeploymentName"] = "deployment"
                })
                .AddEnvironmentVariables()
                .Build();

            var services = new ServiceCollection();
            services.AddSourceIngestionInfrastructure(configuration);
            using var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptions<LlmClientOptions>>().Value;

            Assert.Equal("env-secret", options.ApiKey);
            Assert.Equal("deployment", options.DeploymentName);
            Assert.Equal("https://example.openai.azure.com/openai/v1/", options.Endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Foundry__ApiKey", null);
        }
    }
}
