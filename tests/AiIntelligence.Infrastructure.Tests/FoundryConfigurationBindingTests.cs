using AiIntelligence.Infrastructure.Intelligence;
using Microsoft.Extensions.Configuration;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class FoundryConfigurationBindingTests
{
    [Fact]
    public void JsonConfig_DefinesEndpointAndDeploymentWithoutApiKey()
    {
        var jsonPath = ResolveRepoPath(Path.Combine("src", "AiIntelligence.Console", "appsettings.Development.json"));
        var jsonConfig = new ConfigurationBuilder().AddJsonFile(jsonPath).Build();

        Assert.False(string.IsNullOrWhiteSpace(jsonConfig["Foundry:Endpoint"]));
        Assert.False(string.IsNullOrWhiteSpace(jsonConfig["Foundry:DeploymentName"]));
        Assert.True(string.IsNullOrWhiteSpace(jsonConfig["Foundry:ApiKey"]));
    }

    [Fact]
    public void EnvironmentVariableStyleApiKey_BindsToFoundryOptions()
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

            var options = configuration.GetSection(LlmClientOptions.SectionName).Get<LlmClientOptions>();

            Assert.NotNull(options);
            Assert.Equal("env-secret", options!.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Foundry__ApiKey", null);
        }
    }

    [Fact]
    public void EnvironmentApiKey_OverridesJsonValue()
    {
        Environment.SetEnvironmentVariable("Foundry__ApiKey", "env-secret");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Foundry:Endpoint"] = "https://example.openai.azure.com/openai/v1/",
                    ["Foundry:DeploymentName"] = "deployment",
                    ["Foundry:ApiKey"] = "json-secret"
                })
                .AddEnvironmentVariables()
                .Build();

            var options = configuration.GetSection(LlmClientOptions.SectionName).Get<LlmClientOptions>();

            Assert.NotNull(options);
            Assert.Equal("env-secret", options!.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Foundry__ApiKey", null);
        }
    }

    [Fact]
    public void EnvironmentApiKey_OverridesEmptyJsonValue()
    {
        Environment.SetEnvironmentVariable("Foundry__ApiKey", "env-secret");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Foundry:Endpoint"] = "https://example.openai.azure.com/openai/v1/",
                    ["Foundry:DeploymentName"] = "deployment",
                    ["Foundry:ApiKey"] = ""
                })
                .AddEnvironmentVariables()
                .Build();

            Assert.Equal("env-secret", configuration["Foundry:ApiKey"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Foundry__ApiKey", null);
        }
    }

    private static string ResolveRepoPath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Unable to locate {relativePath} from test output directory.");
    }
}
