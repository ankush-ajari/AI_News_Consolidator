using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Application.Tests;

public sealed class LlmTrendEvidenceExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ConstructsTrendPrompt_WithSchema()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(TrendJson()));
        var extractor = new LlmTrendEvidenceExtractor(llmClient, NullLogger<LlmTrendEvidenceExtractor>.Instance);
        var item = CreateRawItem("Enterprise AI adoption increased in the 2026 report.");

        await extractor.ExtractAsync(item, CancellationToken.None);

        await llmClient.Received(1).CompleteAsync(
            Arg.Is<LLMRequest>(request =>
                request.SystemPrompt.Contains("trend statements", StringComparison.OrdinalIgnoreCase)
                && request.SystemPrompt.Contains("Never present external trend publications as CurrentOfficial", StringComparison.OrdinalIgnoreCase)
                && request.UserPrompt.Contains(item.Url.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                && request.JsonSchemaName == LlmTrendEvidenceExtractor.JsonSchemaName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_MapsTrendExtraction()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(TrendJson()));
        var extractor = new LlmTrendEvidenceExtractor(llmClient, NullLogger<LlmTrendEvidenceExtractor>.Instance);

        var result = await extractor.ExtractAsync(CreateRawItem(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Enterprise AI adoption", item.Topic);
        Assert.Equal("2026", item.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.SourceContent, item.PeriodProvenance);
        Assert.Equal("42%", item.QuantitativeEvidence);
        Assert.Equal("Stanford AI Index", item.PublicationName);
        Assert.Empty(item.ConceptTags);
    }

    [Fact]
    public async Task ExtractAsync_HandlesMissingPeriodAsUnknown()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(TrendJson(period: "")));
        var extractor = new LlmTrendEvidenceExtractor(llmClient, NullLogger<LlmTrendEvidenceExtractor>.Instance);

        var item = Assert.Single((await extractor.ExtractAsync(CreateRawItem(), CancellationToken.None)).Items);

        Assert.Equal("Unknown", item.Period);
        Assert.Equal(TrendEvidencePeriodProvenance.Unknown, item.PeriodProvenance);
    }

    [Fact]
    public async Task ExtractAsync_DistinguishesQualitativeAndQuantitativeEvidence()
    {
        var llmClient = Substitute.For<ILLMClient>();
        llmClient.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse(TrendJson(quantitativeEvidence: "Unknown")));
        var extractor = new LlmTrendEvidenceExtractor(llmClient, NullLogger<LlmTrendEvidenceExtractor>.Instance);

        var item = Assert.Single((await extractor.ExtractAsync(CreateRawItem(), CancellationToken.None)).Items);

        Assert.Equal("Unknown", item.QuantitativeEvidence);
        Assert.Contains("qualitative", item.EvidenceSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static RawSourceItem CreateRawItem(string rawContent = "AI trend content") => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "AI trend report",
        new Uri("https://example.com/report"),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        rawContent,
        new ContentHash("hash"));

    private static string TrendJson(string period = "2026", string quantitativeEvidence = "42%") => $$"""
    {
      "items": [
        {
          "topic": "Enterprise AI adoption",
          "period": "{{period}}",
          "finding": "Enterprise AI adoption increased.",
          "quantitativeEvidence": "{{quantitativeEvidence}}",
          "evidenceSummary": "The publication provides qualitative and quantitative evidence about adoption.",
          "confidence": 0.8,
          "sourceUrl": "https://example.com/report",
          "publicationName": "Stanford AI Index"
        }
      ]
    }
    """;
}
