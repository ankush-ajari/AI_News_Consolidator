using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiIntelligence.Application.Intelligence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiIntelligence.Infrastructure.Intelligence;

public sealed class HttpJsonLlmClient : ILLMClient
{
    public const string HttpClientName = "LlmClient";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<LlmClientOptions> _options;
    private readonly ILogger<HttpJsonLlmClient> _logger;

    public HttpJsonLlmClient(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmClientOptions> options,
        ILogger<HttpJsonLlmClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        ValidateOptions(options);

        var endpoint = BuildEndpoint(options.Endpoint);
        var endpointHost = endpoint.Host;
        var stopwatch = Stopwatch.StartNew();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuthentication(httpRequest, options.ApiKey);
        httpRequest.Content = JsonContent.Create(CreatePayload(options.DeploymentName, request, endpoint), options: JsonOptions);

        _logger.LogInformation(
            "LLM request starting. Deployment: {DeploymentName}; EndpointHost: {EndpointHost}",
            options.DeploymentName,
            endpointHost);

        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogInformation(
            "LLM request completed. Deployment: {DeploymentName}; EndpointHost: {EndpointHost}; HttpStatus: {HttpStatus}; DurationMs: {DurationMs}",
            options.DeploymentName,
            endpointHost,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds);

        if (response.StatusCode == HttpStatusCode.BadRequest
            && endpoint.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                "LLM /responses returned 400. Deployment: {DeploymentName}; EndpointHost: {EndpointHost}; Response: {Response}",
                options.DeploymentName,
                endpointHost,
                errorBody);
            throw new InvalidOperationException("LLM /responses returned 400. The requested operation is unsupported.");
        }

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(responseJson);
        LogTokenUsageIfPresent(document.RootElement, options.DeploymentName, endpointHost);
        var content = ExtractMessageContent(document.RootElement);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LLM response did not contain message content.");
        }

        return new LLMResponse(content);
    }

    private static void ValidateOptions(LlmClientOptions options)
    {
        var missingKeys = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            missingKeys.Add("Foundry:Endpoint");
        }

        if (string.IsNullOrWhiteSpace(options.DeploymentName))
        {
            missingKeys.Add("Foundry:DeploymentName");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            missingKeys.Add("Foundry:ApiKey");
        }

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException($"Foundry LLM configuration is missing required key(s): {string.Join(", ", missingKeys)}.");
        }
    }

    private static Uri BuildEndpoint(string configuredEndpoint)
    {
        var endpoint = new Uri(configuredEndpoint, UriKind.Absolute);
        if (endpoint.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || endpoint.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var baseUri = configuredEndpoint.EndsWith('/') ? configuredEndpoint : configuredEndpoint + "/";
        return new Uri(new Uri(baseUri), "chat/completions");
    }

    private static Uri BuildChatCompletionsEndpoint(Uri endpoint)
    {
        if (endpoint.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var baseUri = endpoint.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            ? endpoint.AbsoluteUri[..^"responses".Length]
            : endpoint.AbsoluteUri;
        var normalizedBase = baseUri.EndsWith('/') ? baseUri : baseUri + "/";
        return new Uri(new Uri(normalizedBase), "chat/completions");
    }

    private static void ApplyAuthentication(HttpRequestMessage httpRequest, string apiKey)
    {
        httpRequest.Headers.Add("api-key", apiKey);
    }

    private void LogTokenUsageIfPresent(JsonElement root, string deploymentName, string endpointHost)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return;
        }

        int? promptTokens = TryGetInt(usage, "prompt_tokens") ?? TryGetInt(usage, "input_tokens");
        int? completionTokens = TryGetInt(usage, "completion_tokens") ?? TryGetInt(usage, "output_tokens");
        int? totalTokens = TryGetInt(usage, "total_tokens");

        _logger.LogInformation(
            "LLM token usage. Deployment: {DeploymentName}; EndpointHost: {EndpointHost}; PromptTokens: {PromptTokens}; CompletionTokens: {CompletionTokens}; TotalTokens: {TotalTokens}",
            deploymentName,
            endpointHost,
            promptTokens,
            completionTokens,
            totalTokens);
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static string? ExtractMessageContent(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            return choices[0].GetProperty("message").GetProperty("content").GetString();
        }

        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var outputItem in output.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static object CreatePayload(string model, LLMRequest request, Uri endpoint)
    {
        return endpoint.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            ? CreateResponsesPayload(model, request)
            : CreateChatCompletionsPayload(model, request);
    }

    private static object CreateChatCompletionsPayload(string model, LLMRequest request)
    {
        object? responseFormat = null;
        if (!string.IsNullOrWhiteSpace(request.JsonSchemaName) && !string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            responseFormat = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = request.JsonSchemaName,
                    strict = true,
                    schema = JsonSerializer.Deserialize<JsonElement>(request.JsonSchema)
                }
            };
        }

        return new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            response_format = responseFormat,
            temperature = 0.1
        };
    }

    private static object CreateResponsesPayload(string model, LLMRequest request)
    {
        object? text = null;
        if (!string.IsNullOrWhiteSpace(request.JsonSchemaName) && !string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = request.JsonSchemaName,
                    strict = true,
                    schema = JsonSerializer.Deserialize<JsonElement>(request.JsonSchema)
                }
            };
        }

        return new
        {
            model,
            instructions = request.SystemPrompt,
            input = request.UserPrompt,
            text
        };
    }
}
