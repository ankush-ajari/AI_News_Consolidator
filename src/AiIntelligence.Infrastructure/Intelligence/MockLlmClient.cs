using System.Text.Json;
using AiIntelligence.Application.Intelligence;

namespace AiIntelligence.Infrastructure.Intelligence;

public sealed class MockLlmClient : ILLMClient
{
    public Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.JsonSchemaName, LlmTrendEvidenceExtractor.JsonSchemaName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new LLMResponse(JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new
                    {
                        topic = "Enterprise AI adoption",
                        period = "Unknown",
                        finding = "Mock trend: source appears to contain reusable AI trend evidence.",
                        quantitativeEvidence = "Unknown",
                        evidenceSummary = "Mock evidence generated for local no-cost pipeline verification.",
                        confidence = 0.5m,
                        sourceUrl = "https://example.com/mock-trend",
                        publicationName = "Mock Trend Publication"
                    }
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }

        var isRelevant = request.UserPrompt.Contains("AI", StringComparison.OrdinalIgnoreCase)
            || request.UserPrompt.Contains("agent", StringComparison.OrdinalIgnoreCase)
            || request.UserPrompt.Contains("model", StringComparison.OrdinalIgnoreCase)
            || request.UserPrompt.Contains("foundry", StringComparison.OrdinalIgnoreCase)
            || request.UserPrompt.Contains("copilot", StringComparison.OrdinalIgnoreCase);

        var response = new
        {
            isRelevantToAI = isRelevant,
            vendor = isRelevant ? "Unknown" : "Unknown",
            topic = isRelevant ? "AI technology development" : "Unknown",
            category = isRelevant ? "Unknown" : "Unknown",
            productOrFramework = "Unknown",
            summary = isRelevant
                ? "Mock analysis: source appears to discuss AI-related technology. Use a real LLM for factual extraction."
                : "Unknown",
            capabilities = isRelevant ? new[] { "Unknown" } : Array.Empty<string>(),
            limitations = new[] { "Unknown" },
            releaseStage = "Unknown",
            relevanceScore = isRelevant ? 0.5m : 0.0m,
            evidenceStatements = isRelevant ? new[] { "Mock evidence: AI-related keyword was present in the supplied source text." } : Array.Empty<string>(),
            suggestedPersonaRelevance = new[]
            {
                new { personaType = "Developer", isRelevant, rationale = isRelevant ? "Mock relevance based on AI-related keywords." : "Unknown" }
            }
        };

        return Task.FromResult(new LLMResponse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }
}
