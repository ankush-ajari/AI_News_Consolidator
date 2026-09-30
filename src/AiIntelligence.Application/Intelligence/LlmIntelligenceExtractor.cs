using System.Text.Json;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Intelligence;

public sealed class LlmIntelligenceExtractor : IIntelligenceExtractor
{
    public const string JsonSchemaName = "ai_intelligence_extraction_result";

    public const string JsonSchema = """
    {
      "type": "object",
      "additionalProperties": false,
      "required": ["isRelevantToAI", "vendor", "topic", "category", "productOrFramework", "summary", "capabilities", "limitations", "releaseStage", "relevanceScore", "evidenceStatements", "suggestedPersonaRelevance"],
      "properties": {
        "isRelevantToAI": { "type": "boolean" },
        "vendor": { "type": "string" },
        "topic": { "type": "string" },
        "category": { "type": "string" },
        "productOrFramework": { "type": "string" },
        "summary": { "type": "string" },
        "capabilities": { "type": "array", "items": { "type": "string" } },
        "limitations": { "type": "array", "items": { "type": "string" } },
        "releaseStage": { "type": "string" },
        "relevanceScore": { "type": "number", "minimum": 0, "maximum": 1 },
        "evidenceStatements": { "type": "array", "items": { "type": "string" } },
        "suggestedPersonaRelevance": {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "required": ["personaType", "isRelevant", "rationale"],
            "properties": {
              "personaType": { "type": "string", "enum": ["Developer", "QA", "BusinessAnalyst", "ProjectManager", "Sales"] },
              "isRelevant": { "type": "boolean" },
              "rationale": { "type": "string" }
            }
          }
        }
      }
    }
    """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILLMClient _llmClient;
    private readonly ILogger<LlmIntelligenceExtractor> _logger;

    public LlmIntelligenceExtractor(ILLMClient llmClient, ILogger<LlmIntelligenceExtractor> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<IntelligenceExtractionResult> ExtractAsync(RawSourceItem sourceItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceItem);

        var response = await _llmClient.CompleteAsync(
            new LLMRequest(BuildSystemPrompt(), BuildUserPrompt(sourceItem), JsonSchemaName, JsonSchema),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var dto = JsonSerializer.Deserialize<IntelligenceExtractionDto>(response.Content, JsonOptions)
                ?? throw new InvalidOperationException("LLM returned an empty response.");

            return dto.ToResult();
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "LLM returned malformed JSON for source item {SourceItemId}.", sourceItem.Id);
            throw new InvalidOperationException("LLM returned malformed structured intelligence JSON.", exception);
        }
    }

    public static string BuildSystemPrompt()
    {
        return """
        You extract structured AI technology intelligence from supplied source content.
        Rules:
        - Never invent facts not present in the supplied source content.
        - Distinguish source statements from interpretations; evidenceStatements must be directly supported by the source text.
        - Retain the original source URL in the reasoning context; do not alter source identity.
        - Return "Unknown" where evidence is insufficient.
        - Do not convert analyst opinion, marketing claims, or commentary into vendor fact.
        - Do not classify advertisements, navigation text, boilerplate, or unrelated content as AI intelligence.
        - If the source is unrelated to AI technology developments, set isRelevantToAI=false, relevanceScore<=0.2, and use "Unknown" for insufficient fields.
        - Output only JSON conforming to the provided schema.
        """;
    }

    public static string BuildUserPrompt(RawSourceItem sourceItem)
    {
        var content = string.IsNullOrWhiteSpace(sourceItem.EnrichedContent)
            ? sourceItem.RawContent
            : string.Concat(sourceItem.RawContent, "\n\nEnriched source content:\n", sourceItem.EnrichedContent);

        return $$"""
        Source item:
        Title: {{sourceItem.Title}}
        Source URL: {{sourceItem.Url}}
        PublishedAt: {{sourceItem.PublishedAt?.ToString("O") ?? "Unknown"}}
        FetchedAt: {{sourceItem.FetchedAt:O}}

        Source content:
        {{content}}
        """;
    }

    private sealed record IntelligenceExtractionDto(
        bool IsRelevantToAI,
        string? Vendor,
        string? Topic,
        string? Category,
        string? ProductOrFramework,
        string? Summary,
        IReadOnlyCollection<string>? Capabilities,
        IReadOnlyCollection<string>? Limitations,
        string? ReleaseStage,
        decimal RelevanceScore,
        IReadOnlyCollection<string>? EvidenceStatements,
        IReadOnlyCollection<PersonaRelevanceDto>? SuggestedPersonaRelevance)
    {
        public IntelligenceExtractionResult ToResult()
        {
            return new IntelligenceExtractionResult(
                IsRelevantToAI,
                OrUnknown(Vendor),
                OrUnknown(Topic),
                OrUnknown(Category),
                OrUnknown(ProductOrFramework),
                OrUnknown(Summary),
                Normalize(Capabilities),
                Normalize(Limitations),
                OrUnknown(ReleaseStage),
                Math.Clamp(RelevanceScore, 0, 1),
                Normalize(EvidenceStatements),
                SuggestedPersonaRelevance?.Select(item => item.ToResult()).ToArray() ?? Array.Empty<SuggestedPersonaRelevance>());
        }

        private static string OrUnknown(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

        private static IReadOnlyCollection<string> Normalize(IReadOnlyCollection<string>? values)
        {
            return values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray()
                ?? Array.Empty<string>();
        }
    }

    private sealed record PersonaRelevanceDto(string? PersonaType, bool IsRelevant, string? Rationale)
    {
        public SuggestedPersonaRelevance ToResult()
        {
            var personaType = Enum.TryParse<global::AiIntelligence.Domain.Enums.PersonaType>(PersonaType, ignoreCase: true, out var parsed)
                ? parsed
                : global::AiIntelligence.Domain.Enums.PersonaType.BusinessAnalyst;

            return new SuggestedPersonaRelevance(personaType, IsRelevant, string.IsNullOrWhiteSpace(Rationale) ? "Unknown" : Rationale.Trim());
        }
    }
}
