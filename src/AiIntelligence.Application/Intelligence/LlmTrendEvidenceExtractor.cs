using System.Text.Json;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Intelligence;

public sealed class LlmTrendEvidenceExtractor : ITrendEvidenceExtractor
{
    public const string JsonSchemaName = "trend_evidence_extraction_result";

    public const string JsonSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["items"],
      "properties": {
        "items": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["topic", "period", "finding", "quantitativeEvidence", "evidenceSummary", "confidence", "sourceUrl", "publicationName"],
            "properties": {
              "topic": { "type": "string" },
              "period": { "type": "string" },
              "finding": { "type": "string" },
              "quantitativeEvidence": { "type": "string" },
              "evidenceSummary": { "type": "string" },
              "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
              "sourceUrl": { "type": "string" },
              "publicationName": { "type": "string" }
            }
          }
        }
      }
    }
    """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILLMClient _llmClient;
    private readonly ILogger<LlmTrendEvidenceExtractor> _logger;

    public LlmTrendEvidenceExtractor(ILLMClient llmClient, ILogger<LlmTrendEvidenceExtractor> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<TrendEvidenceExtractionBatch> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceItem);

        var response = await _llmClient.CompleteAsync(
            new LLMRequest(BuildSystemPrompt(), BuildUserPrompt(sourceItem), JsonSchemaName, JsonSchema),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var dto = JsonSerializer.Deserialize<TrendEvidenceExtractionBatchDto>(response.Content, JsonOptions)
                ?? throw new InvalidOperationException("LLM returned an empty trend extraction response.");

            return dto.ToResult(sourceItem.Url);
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "LLM returned malformed trend JSON for source item {SourceItemId}.", sourceItem.Id);
            throw new InvalidOperationException("LLM returned malformed structured trend evidence JSON.", exception);
        }
    }

    public static string BuildSystemPrompt()
    {
        return """
        You extract reusable AI trend evidence from external trend/research publications.
        Rules:
        - Extract trend statements, not a whole-report summary.
        - Never present external trend publications as CurrentOfficial evidence.
        - Every item must preserve provenance: publicationName, sourceUrl, and period.
        - Return "Unknown" when period, publication, or quantitative evidence is insufficient.
        - Distinguish qualitative findings from quantitative evidence.
        - Do not invent numbers, benchmarks, vendors, dates, or claims not present in the source text.
        - Prefer reusable trend statements such as enterprise AI adoption growth, agent benchmark improvement, inference cost reduction, increased investment, smaller model growth, or reasoning/coding capability development.
        - Output only JSON conforming to the provided schema.
        """;
    }

    public static string BuildUserPrompt(RawSourceItem sourceItem)
    {
        var content = string.IsNullOrWhiteSpace(sourceItem.EnrichedContent)
            ? sourceItem.RawContent
            : string.Concat(sourceItem.RawContent, "\n\nEnriched source content:\n", sourceItem.EnrichedContent);

        return $$"""
        Trend research source item:
        Title: {{sourceItem.Title}}
        Source URL: {{sourceItem.Url}}
        PublishedAt: {{sourceItem.PublishedAt?.ToString("O") ?? "Unknown"}}
        FetchedAt: {{sourceItem.FetchedAt:O}}

        Source content:
        {{content}}
        """;
    }

    private sealed record TrendEvidenceExtractionBatchDto(IReadOnlyCollection<TrendEvidenceExtractionDto>? Items)
    {
        public TrendEvidenceExtractionBatch ToResult(Uri fallbackSourceUrl)
        {
            return new TrendEvidenceExtractionBatch(Items?.Select(item => item.ToResult(fallbackSourceUrl)).ToArray() ?? Array.Empty<TrendEvidenceExtractionResult>());
        }
    }

    private sealed record TrendEvidenceExtractionDto(
        string? Topic,
        string? Period,
        string? Finding,
        string? QuantitativeEvidence,
        string? EvidenceSummary,
        decimal Confidence,
        string? SourceUrl,
        string? PublicationName)
    {
        public TrendEvidenceExtractionResult ToResult(Uri fallbackSourceUrl)
        {
            var sourceUrl = Uri.TryCreate(SourceUrl, UriKind.Absolute, out var parsedUrl) ? parsedUrl : fallbackSourceUrl;
            var period = OrUnknown(Period);
            var provenance = period.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                ? TrendEvidencePeriodProvenance.Unknown
                : TrendEvidencePeriodProvenance.SourceContent;
            return new TrendEvidenceExtractionResult(
                OrUnknown(Topic),
                period,
                provenance,
                OrUnknown(Finding),
                OrUnknown(QuantitativeEvidence),
                OrUnknown(EvidenceSummary),
                Math.Clamp(Confidence, 0, 1),
                sourceUrl,
                OrUnknown(PublicationName));
        }

        private static string OrUnknown(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }
}
