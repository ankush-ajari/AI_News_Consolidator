using AiIntelligence.Application.Reporting;
using AiIntelligence.Console;
using AiIntelligence.Infrastructure.Intelligence;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class ConsoleProgramLlmModeTests
{
    [Fact]
    public void Report_UsesMockPersonaGenerator_WhenApiKeyMissing()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:"
        });
        var mode = ConsoleLlmModeDecider.ShouldUseMock("report", mockFlag: false, hasApiKey: false);
        if (mode)
        {
            services.AddScoped<AiIntelligence.Application.Intelligence.ILLMClient, MockLlmClient>();
            services.AddScoped<ITrendCorrelationService, MockTrendCorrelationService>();
            services.AddScoped<IPersonaReportGenerator, MockPersonaReportGenerator>();
        }
        using var provider = services.BuildServiceProvider();

        var generator = provider.GetRequiredService<IPersonaReportGenerator>();

        Assert.IsType<MockPersonaReportGenerator>(generator);
    }

    [Fact]
    public async Task AnalyzeWithoutMock_FailsWhenApiKeyMissing()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:"
        });

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<AiIntelligence.Application.Intelligence.ILLMClient>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(
            new AiIntelligence.Application.Intelligence.LLMRequest("system", "user", null, null),
            CancellationToken.None));

        Assert.Contains("Foundry:ApiKey", exception.Message);
    }

    [Fact]
    public async Task AnalyzeWithMock_WorksWhenApiKeyMissing()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:"
        });
        services.AddScoped<AiIntelligence.Application.Intelligence.ILLMClient, MockLlmClient>();
        services.AddScoped<ITrendCorrelationService, MockTrendCorrelationService>();
        services.AddScoped<IPersonaReportGenerator, MockPersonaReportGenerator>();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<AiIntelligence.Application.Intelligence.ILLMClient>();

        var response = await client.CompleteAsync(
            new AiIntelligence.Application.Intelligence.LLMRequest("system", "AI", null, null),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Content));
    }

    [Fact]
    public void InspectAndReset_DoNotRequireApiKey_WhenNotUsingLlm()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:"
        });
        var inspectMode = ConsoleLlmModeDecider.ShouldUseMock("inspect", mockFlag: false, hasApiKey: false);
        var resetMode = ConsoleLlmModeDecider.ShouldUseMock("reset", mockFlag: false, hasApiKey: false);

        Assert.False(inspectMode);
        Assert.False(resetMode);
    }

    private static IServiceCollection BuildServices(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSourceIngestionInfrastructure(configuration);
        return services;
    }
}
