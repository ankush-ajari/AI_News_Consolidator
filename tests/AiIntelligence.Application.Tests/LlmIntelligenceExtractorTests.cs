using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Application.Tests;

public sealed class LlmIntelligenceExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ConstructsGroundedPrompt_WithSchemaRequest()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(ValidJson()));
        var extractor = new LlmIntelligenceExtractor(llmClient, NullLogger<LlmIntelligenceExtractor>.Instance);
        var item = CreateRawItem(rawContent: "Source states a new AI model capability.", enrichedContent: "More source detail.");

        await extractor.ExtractAsync(item, CancellationToken.None);

        await llmClient.Received(1).CompleteAsync(
            Arg.Is<LLMRequest>(request =>
                request.SystemPrompt.Contains("Never invent facts", StringComparison.OrdinalIgnoreCase)
                && request.SystemPrompt.Contains("Return \"Unknown\"", StringComparison.OrdinalIgnoreCase)
                && request.UserPrompt.Contains(item.Url.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                && request.UserPrompt.Contains("More source detail", StringComparison.OrdinalIgnoreCase)
                && request.JsonSchemaName == LlmIntelligenceExtractor.JsonSchemaName
                && request.JsonSchema != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_MapsSuccessfulJson()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(ValidJson()));
        var extractor = new LlmIntelligenceExtractor(llmClient, NullLogger<LlmIntelligenceExtractor>.Instance);

        var result = await extractor.ExtractAsync(CreateRawItem(), CancellationToken.None);

        Assert.True(result.IsRelevantToAI);
        Assert.Equal("Microsoft", result.Vendor);
        Assert.Equal("Agent Framework", result.ProductOrFramework);
        Assert.Contains("Tool orchestration", result.Capabilities);
        Assert.Contains(result.SuggestedPersonaRelevance, item => item.PersonaType == Domain.Enums.PersonaType.Developer);
    }

    [Fact]
    public async Task ExtractAsync_ThrowsForMalformedOutput()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("not json"));
        var extractor = new LlmIntelligenceExtractor(llmClient, NullLogger<LlmIntelligenceExtractor>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => extractor.ExtractAsync(CreateRawItem(), CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_PreservesIrrelevantClassification()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(ValidJson(isRelevant: false)));
        var extractor = new LlmIntelligenceExtractor(llmClient, NullLogger<LlmIntelligenceExtractor>.Instance);

        var result = await extractor.ExtractAsync(CreateRawItem(), CancellationToken.None);

        Assert.False(result.IsRelevantToAI);
    }

    [Fact]
    public async Task ExtractAsync_UsesUnknownForMissingLimitationEvidence()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "isRelevantToAI": true,
          "vendor": "Microsoft",
          "topic": "Agents",
          "category": "Framework",
          "productOrFramework": "Agent Framework",
          "summary": "A capability was announced.",
          "capabilities": ["Tool orchestration"],
          "limitations": ["Unknown"],
          "releaseStage": "Unknown",
          "relevanceScore": 0.8,
          "evidenceStatements": ["The source mentions agent orchestration."],
          "suggestedPersonaRelevance": []
        }
        """));
        var extractor = new LlmIntelligenceExtractor(llmClient, NullLogger<LlmIntelligenceExtractor>.Instance);

        var result = await extractor.ExtractAsync(CreateRawItem(), CancellationToken.None);

        Assert.Contains("Unknown", result.Limitations);
    }

    private static RawSourceItem CreateRawItem(string rawContent = "AI content", string enrichedContent = "") => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "AI release",
        new Uri("https://example.com/ai-release"),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        rawContent,
        new ContentHash("hash"),
        enrichedContent);

    private static string ValidJson(bool isRelevant = true) => $$"""
    {
      "isRelevantToAI": {{isRelevant.ToString().ToLowerInvariant()}},
      "vendor": "Microsoft",
      "topic": "Agents",
      "category": "Framework",
      "productOrFramework": "Agent Framework",
      "summary": "A new AI framework capability was announced.",
      "capabilities": ["Tool orchestration"],
      "limitations": ["Unknown"],
      "releaseStage": "Preview",
      "relevanceScore": 0.9,
      "evidenceStatements": ["The source states tool orchestration support."],
      "suggestedPersonaRelevance": [
        { "personaType": "Developer", "isRelevant": true, "rationale": "Developers may use the framework." }
      ]
    }
    """;
}
