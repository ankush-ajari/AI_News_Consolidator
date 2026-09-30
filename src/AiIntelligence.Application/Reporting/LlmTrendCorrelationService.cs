using System.Text.Json;
using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class LlmTrendCorrelationService : ITrendCorrelationService
{
    public const string JsonSchemaName = "trend_correlation_result";

    public const string JsonSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["relationship", "currentDevelopment", "relatedTrend", "explanation", "confidence", "supportingSourceUrls"],
      "properties": {
        "relationship": { "type": "string", "enum": ["Supports", "Extends", "Contradicts", "InsufficientEvidence"] },
        "currentDevelopment": { "type": "string" },
        "relatedTrend": { "type": "string" },
        "explanation": { "type": "string" },
        "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
        "supportingSourceUrls": { "type": "array", "items": { "type": "string" } }
      }
    }
    """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILLMClient _llmClient;
    private readonly ILogger<LlmTrendCorrelationService> _logger;

    public LlmTrendCorrelationService(ILLMClient llmClient, ILogger<LlmTrendCorrelationService> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<TrendCorrelation> CorrelateAsync(
        IntelligenceItem intelligenceItem,
        IReadOnlyCollection<TrendEvidence> candidateTrendEvidence,
        CancellationToken cancellationToken)
    {
        if (candidateTrendEvidence.Count == 0)
        {
            return Insufficient(intelligenceItem, "No deterministic trend candidates were selected.");
        }

        var hasSameSourceEvidence = candidateTrendEvidence.Any(candidate => candidate.SourceUrl == intelligenceItem.SourceUrl);
        var response = await _llmClient.CompleteAsync(
            new LLMRequest(BuildSystemPrompt(), BuildUserPrompt(intelligenceItem, candidateTrendEvidence, hasSameSourceEvidence), JsonSchemaName, JsonSchema),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var dto = JsonSerializer.Deserialize<CorrelationDto>(response.Content, JsonOptions)
                ?? throw new InvalidOperationException("LLM returned an empty correlation response.");
            return dto.ToCorrelation(intelligenceItem, hasSameSourceEvidence);
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Malformed LLM correlation JSON for intelligence item {IntelligenceItemId}.", intelligenceItem.Id);
            throw new InvalidOperationException("LLM returned malformed trend correlation JSON.", exception);
        }
    }

    public static string BuildSystemPrompt() => """
        Correlate current AI developments with supplied trend evidence only.
        Rules:
        - Use only supplied IntelligenceItem and candidate TrendEvidence.
        - Do not search the web.
        - Do not introduce external knowledge.
        - Do not invent relationships.
        - Distinguish source fact from interpretation.
        - Preserve source provenance URLs.
        - Use InsufficientEvidence when correlation is weak.
        - Do not treat analyst/research interpretation as an official vendor statement.
        - If any candidate evidence is from the same source URL as the IntelligenceItem, the explanation must explicitly state that the evidence is from the same source.
        - Keep explanations evidence-grounded and concise.
        - Output only JSON matching the schema.
        """;

    public static string BuildUserPrompt(IntelligenceItem item, IReadOnlyCollection<TrendEvidence> candidates, bool hasSameSourceEvidence)
    {
        var trends = string.Join("\n", candidates.Select((trend, index) =>
            $"Trend {index + 1}: Topic={trend.Topic}; Period={trend.Period}; Finding={trend.Finding}; EvidenceSummary={trend.EvidenceSummary}; Confidence={trend.Confidence}; SourceUrl={trend.SourceUrl}"));

        var sameSourceNote = hasSameSourceEvidence
            ? "Same-source evidence is present. Explicitly state when the evidence comes from the same source as the IntelligenceItem."
            : "";

        return $$"""
        IntelligenceItem:
        Id: {{item.Id}}
        Vendor: {{item.Vendor}}
        Topic: {{item.Topic}}
        Category: {{item.Category}}
        ProductOrFramework: {{item.ProductOrFramework}}
        Summary: {{item.Summary}}
        SourceUrl: {{item.SourceUrl}}

        {{sameSourceNote}}

        Candidate TrendEvidence:
        {{trends}}
        """;
    }

    private static TrendCorrelation Insufficient(IntelligenceItem item, string explanation) => new(
        item.Id,
        item.Summary,
        "Unknown",
        CorrelationRelationship.InsufficientEvidence,
        explanation,
        explanation,
        0,
        new[] { item.SourceUrl },
        false);

    private sealed record CorrelationDto(
        string? Relationship,
        string? CurrentDevelopment,
        string? RelatedTrend,
        string? Explanation,
        decimal Confidence,
        IReadOnlyCollection<string>? SupportingSourceUrls)
    {
        public TrendCorrelation ToCorrelation(IntelligenceItem item, bool sameSourceEvidence)
        {
            var relationship = Enum.TryParse<CorrelationRelationship>(Relationship, ignoreCase: true, out var parsed)
                ? parsed
                : CorrelationRelationship.InsufficientEvidence;
            var urls = SupportingSourceUrls?
                .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
                .Where(uri => uri is not null)
                .Cast<Uri>()
                .ToArray() ?? Array.Empty<Uri>();

            if (!urls.Contains(item.SourceUrl))
            {
                urls = urls.Append(item.SourceUrl).ToArray();
            }

            var confidence = Math.Clamp(Confidence, 0, 1);
            if (sameSourceEvidence)
            {
                confidence = Math.Clamp(confidence - 0.1m, 0, 1);
            }

            var explanation = string.IsNullOrWhiteSpace(Explanation) ? "Unknown" : Explanation.Trim();
            if (sameSourceEvidence && !explanation.Contains("same source", StringComparison.OrdinalIgnoreCase))
            {
                explanation = string.Concat(explanation, " Evidence comes from the same source as the current development.");
            }

            var evidenceBasis = explanation.Length > 220
                ? string.Concat(explanation.AsSpan(0, 220).TrimEnd().ToString(), "…")
                : explanation;

            return new TrendCorrelation(
                item.Id,
                string.IsNullOrWhiteSpace(CurrentDevelopment) ? item.Summary : CurrentDevelopment.Trim(),
                string.IsNullOrWhiteSpace(RelatedTrend) ? "Unknown" : RelatedTrend.Trim(),
                relationship,
                explanation,
                evidenceBasis,
                confidence,
                urls,
                sameSourceEvidence);
        }
    }
}
