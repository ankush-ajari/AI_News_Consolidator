using System.Net;
using AiIntelligence.Application.Intelligence;
using AiIntelligence.Infrastructure.Intelligence;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class HttpJsonLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_ThrowsClearError_WhenApiKeyMissing()
    {
        var client = CreateClient(new LlmClientOptions
        {
            Endpoint = "https://example.openai.azure.com/openai/v1/",
            DeploymentName = "deployment",
            ApiKey = ""
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(
            new LLMRequest("system", "user", null, null),
            CancellationToken.None));

        Assert.Contains("Foundry:ApiKey", exception.Message);
        Assert.DoesNotContain("api-key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_SendsConfiguredApiKeyHeader()
    {
        const string apiKey = "secret-test-key";
        var handler = StubHttpMessageHandler.WithStringResponse("""
        { "choices": [ { "message": { "content": "MODEL_OK" } } ], "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 } }
        """);
        var client = CreateClient(new LlmClientOptions
        {
            Endpoint = "https://example.openai.azure.com/openai/v1/",
            DeploymentName = "deployment",
            ApiKey = apiKey
        }, handler);

        var response = await client.CompleteAsync(new LLMRequest("system", "Return exactly: MODEL_OK", null, null), CancellationToken.None);

        Assert.Equal("MODEL_OK", response.Content);
        Assert.True(handler.LastRequest!.Headers.TryGetValues("api-key", out var values));
        Assert.Equal(apiKey, values.Single());
        Assert.EndsWith("/openai/v1/chat/completions", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task CompleteAsync_ParsesResponsesApiOutputContentText()
    {
        var handler = StubHttpMessageHandler.WithStringResponse("""
        {
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "MODEL_OK" }
              ]
            }
          ]
        }
        """);
        var client = CreateClient(new LlmClientOptions
        {
            Endpoint = "https://example.openai.azure.com/openai/v1/responses",
            DeploymentName = "deployment",
            ApiKey = "secret-test-key"
        }, handler);

        var response = await client.CompleteAsync(new LLMRequest("system", "user", null, null), CancellationToken.None);

        Assert.Equal("MODEL_OK", response.Content);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotLogApiKey()
    {
        const string apiKey = "secret-test-key";
        var logger = new CapturingLogger<HttpJsonLlmClient>();
        var handler = StubHttpMessageHandler.WithStringResponse("""
        { "choices": [ { "message": { "content": "MODEL_OK" } } ] }
        """);
        var client = CreateClient(new LlmClientOptions
        {
            Endpoint = "https://example.openai.azure.com/openai/v1/",
            DeploymentName = "deployment",
            ApiKey = apiKey
        }, handler, logger);

        await client.CompleteAsync(new LLMRequest("system", "user", null, null), CancellationToken.None);

        Assert.DoesNotContain(logger.Messages, message => message.Contains(apiKey, StringComparison.Ordinal));
    }

    [Fact]
    public void DependencyInjection_UsesRealClientByDefault_AndMockWhenOverridden()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:",
                ["Foundry:Endpoint"] = "https://example.openai.azure.com/openai/v1/",
                ["Foundry:DeploymentName"] = "deployment",
                ["Foundry:ApiKey"] = "secret"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSourceIngestionInfrastructure(configuration);
        using var realProvider = services.BuildServiceProvider();
        Assert.IsType<HttpJsonLlmClient>(realProvider.GetRequiredService<ILLMClient>());

        services.AddScoped<ILLMClient, MockLlmClient>();
        using var mockProvider = services.BuildServiceProvider();
        Assert.IsType<MockLlmClient>(mockProvider.GetRequiredService<ILLMClient>());
    }

    [Fact]
    public void Configuration_BindsApiKey_FromEnvironmentVariables()
    {
        const string apiKey = "secret-from-env";
        Environment.SetEnvironmentVariable("Foundry__ApiKey", apiKey);
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
            Assert.Equal(apiKey, options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Foundry__ApiKey", null);
        }
    }

    private static HttpJsonLlmClient CreateClient(
        LlmClientOptions options,
        StubHttpMessageHandler? handler = null,
        ILogger<HttpJsonLlmClient>? logger = null)
    {
        handler ??= new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"choices\": [ { \"message\": { \"content\": \"MODEL_OK\" } } ] }")
        }));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpJsonLlmClient.HttpClientName).Returns(new HttpClient(handler));
        return new HttpJsonLlmClient(factory, Options.Create(options), logger ?? new CapturingLogger<HttpJsonLlmClient>());
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
