using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Application.Tests;

public sealed class ReportingTests
{
        private const string PersonaResponseJson = """
        {
            "relevantDevelopment": "Persona JSON parsed.",
            "specificImpact": "Specific impact.",
            "trendImplication": "Trend context.",
            "recommendedActions": ["Action one"],
            "questionsToExplore": ["Question one"],
            "watchItems": ["Watch one"],
            "relevance": "Relevant."
        }
        """;

    [Fact]
    public async Task CandidateSelector_SelectsRelatedEvidence_ExcludesUnrelated_AndRespectsMax()
    {
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Agent evaluation", "Agent benchmark performance improved."),
            Trend("Model inference", "Inference cost reduction continued."),
            Trend("Space systems", "Satellite launch cadence increased.")
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);
        var item = Intelligence(topic: "Agent evaluation", category: "AI Agent", product: "Agent SDK");

        var selection = await selector.SelectCandidatesAsync(item, maxCandidates: 1, CancellationToken.None);

        var selected = Assert.Single(selection.Candidates);
        Assert.Contains("Agent", selected.Topic);
        Assert.DoesNotContain(selection.Candidates, candidate => candidate.Topic == "Space systems");
    }

    [Fact]
    public async Task CandidateSelector_ReturnsEmpty_WhenNoRelatedEvidence()
    {
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { Trend("Space systems", "Satellite launch cadence increased.") });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(Intelligence("AI regulation", "Governance", "Policy"), 5, CancellationToken.None);

        Assert.DoesNotContain(selection.Candidates, candidate => candidate.Topic == "Space systems");
    }

    [Theory]
    [InlineData("Supports", CorrelationRelationship.Supports)]
    [InlineData("Extends", CorrelationRelationship.Extends)]
    [InlineData("Contradicts", CorrelationRelationship.Contradicts)]
    [InlineData("InsufficientEvidence", CorrelationRelationship.InsufficientEvidence)]
    public async Task CorrelationService_MapsRelationship_AndRetainsSourceUrls(string relationshipText, CorrelationRelationship expected)
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse($$"""
        {
          "relationship": "{{relationshipText}}",
          "currentDevelopment": "Current dev",
          "relatedTrend": "Trend",
          "explanation": "Explanation",
          "confidence": 0.7,
          "supportingSourceUrls": ["https://trend.example.com"]
        }
        """));
        var service = new LlmTrendCorrelationService(llm, NullLogger<LlmTrendCorrelationService>.Instance);
        var intelligence = Intelligence();

        var correlation = await service.CorrelateAsync(intelligence, new[] { Trend("Agent", "Agent trend") }, CancellationToken.None);

        Assert.Equal(expected, correlation.Relationship);
        Assert.Contains(correlation.SupportingSourceUrls, uri => uri.AbsoluteUri == "https://trend.example.com/");
        Assert.Contains(correlation.SupportingSourceUrls, uri => uri == intelligence.SourceUrl);
        Assert.False(string.IsNullOrWhiteSpace(correlation.EvidenceBasis));
    }

    [Fact]
    public async Task CorrelationService_RejectsMalformedLlmOutput()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("not-json"));
        var service = new LlmTrendCorrelationService(llm, NullLogger<LlmTrendCorrelationService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrelateAsync(Intelligence(), new[] { Trend("Agent", "Agent trend") }, CancellationToken.None));
    }

    [Fact]
    public async Task CorrelationService_NoCandidates_ReturnsInsufficientEvidence_WithoutLlmCall()
    {
        var llm = Substitute.For<ILLMClient>();
        var service = new LlmTrendCorrelationService(llm, NullLogger<LlmTrendCorrelationService>.Instance);

        var correlation = await service.CorrelateAsync(Intelligence(), Array.Empty<TrendEvidence>(), CancellationToken.None);

        Assert.Equal(CorrelationRelationship.InsufficientEvidence, correlation.Relationship);
        Assert.False(string.IsNullOrWhiteSpace(correlation.EvidenceBasis));
        await llm.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    [Fact]
    public void PersonaPrompts_DifferByRole_AndRetainReferences()
    {
        var intelligence = new[] { Intelligence() };
        var trends = new[] { Trend("Agent", "Agent benchmark performance improved.") };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Current", "Trend", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl, trends[0].SourceUrl }, false) };

        var developerPrompt = LlmPersonaReportGenerator.BuildPersonaPrompt(PersonaType.Developer, PersonaRelevance.High, intelligence, correlations, trends);
        var qaPrompt = LlmPersonaReportGenerator.BuildPersonaPrompt(PersonaType.QA, PersonaRelevance.High, intelligence, correlations, trends);
        var salesPrompt = LlmPersonaReportGenerator.BuildPersonaPrompt(PersonaType.Sales, PersonaRelevance.Low, intelligence, correlations, trends);
        var pmPrompt = LlmPersonaReportGenerator.BuildPersonaPrompt(PersonaType.ProjectManager, PersonaRelevance.Medium, intelligence, correlations, trends);

        Assert.NotEqual(developerPrompt, qaPrompt);
        Assert.NotEqual(salesPrompt, pmPrompt);
        Assert.Contains("current.example.com", developerPrompt);
        Assert.Contains("trend.example.com", developerPrompt);
    }

    [Fact]
    public async Task PersonaGeneration_ProducesRoleSpecificOutputs()
    {
        var intelligence = new[] { Intelligence() };
        var trends = new[] { Trend("Agent", "Agent benchmark performance improved.") };
        var correlations = new[] { await new MockTrendCorrelationService().CorrelateAsync(intelligence[0], trends, CancellationToken.None) };

        var document = await new MockPersonaReportGenerator().GenerateAsync(intelligence, correlations, trends, CancellationToken.None);

        var developer = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Developer);
        var qa = document.PersonaSections.Single(section => section.PersonaType == PersonaType.QA);
        var ba = document.PersonaSections.Single(section => section.PersonaType == PersonaType.BusinessAnalyst);
        var pm = document.PersonaSections.Single(section => section.PersonaType == PersonaType.ProjectManager);
        var sales = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Sales);

        Assert.NotEqual(developer.SpecificImpact, qa.SpecificImpact);
        Assert.NotEqual(qa.SpecificImpact, sales.SpecificImpact);
        Assert.DoesNotContain(developer.RecommendedActions, action => action.Contains("customer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sales.RecommendedActions, action => action.Contains("SDK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("acceptance criteria", string.Join(' ', ba.RecommendedActions), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delivery risk", string.Join(' ', pm.RecommendedActions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PersonaGeneration_GatesOutput_ByRelevance()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "SDK release announced.",
          "specificImpact": "Large impact on workflows with additional integration steps.",
          "trendImplication": "May accelerate adoption.",
          "recommendedActions": ["Action one", "Action two", "Action three"],
          "questionsToExplore": ["Question one", "Question two"],
          "watchItems": ["Watch one", "Watch two"],
          "relevance": "Mock"
        }
        """));

        var generator = new LlmPersonaReportGenerator(llm, Substitute.For<IRawSourceRepository>(), NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(summary: "New SDK framework for agents released.") };
        var trends = new[] { Trend("Agent adoption", "Workforce adoption trend.") };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "SDK", "Adoption", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl, trends[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, trends, CancellationToken.None);

        var developer = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Developer);
        var sales = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Sales);

        Assert.True(developer.Relevance is PersonaRelevance.High or PersonaRelevance.Medium);
        Assert.NotEmpty(developer.RecommendedActions);

        Assert.True(sales.Relevance is PersonaRelevance.Low or PersonaRelevance.Medium);
        Assert.InRange(sales.RecommendedActions.Count, 1, 3);
    }

    [Fact]
    public async Task PersonaGeneration_NotRelevant_ProducesNoActions()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Unrelated update.",
          "specificImpact": "No impact.",
          "trendImplication": "None.",
          "recommendedActions": ["Invented action"],
          "questionsToExplore": ["Invented question"],
          "watchItems": ["Invented watch"],
          "relevance": "Mock"
        }
        """));

        var generator = new LlmPersonaReportGenerator(llm, Substitute.For<IRawSourceRepository>(), NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(summary: "Minor admin operations update.") };
        var trends = Array.Empty<TrendEvidence>();
        var correlations = Array.Empty<TrendCorrelation>();

        var document = await generator.GenerateAsync(intelligence, correlations, trends, CancellationToken.None);

        var sales = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Sales);
        Assert.Equal(PersonaRelevance.NotRelevant, sales.Relevance);
        Assert.Empty(sales.RecommendedActions);
        Assert.Empty(sales.QuestionsToExplore);
        Assert.Empty(sales.WatchItems);
    }

    [Fact]
    public async Task PersonaGeneration_ParsesValidJson_AndRequestsStrictSchema()
    {
        var (document, llm) = await GeneratePersonaDocumentAsync(PersonaResponseJson);

        Assert.Equal(5, document.PersonaSections.Count);
        Assert.All(document.PersonaSections, section => Assert.DoesNotContain("FALLBACK:", section.RelevanceDetail));
        Assert.Equal("Persona JSON parsed.", document.PersonaSections.Single(section => section.PersonaType == PersonaType.Developer).RelevantDevelopment);
        await llm.Received(5).CompleteAsync(
            Arg.Is<LLMRequest>(request =>
                request.JsonSchemaName == LlmPersonaReportGenerator.PersonaSectionJsonSchemaName
                && request.JsonSchema != null
                && request.JsonSchema.Contains("additionalProperties", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersonaGeneration_ParsesFencedJson_WithSurroundingExplanation()
    {
        var response = $"Persona result follows:\n```json\n{PersonaResponseJson}\n```\nEnd of result.";

        var (document, _) = await GeneratePersonaDocumentAsync(response);

        Assert.All(document.PersonaSections, section => Assert.DoesNotContain("FALLBACK:", section.RelevanceDetail));
        Assert.Equal("Persona JSON parsed.", document.PersonaSections.Single(section => section.PersonaType == PersonaType.Developer).RelevantDevelopment);
    }

    [Fact]
    public async Task PersonaGeneration_ParsesJson_WithSurroundingWhitespace()
    {
        var response = $" \r\n\t{PersonaResponseJson}\n \t";

        var (document, _) = await GeneratePersonaDocumentAsync(response);

        Assert.All(document.PersonaSections, section => Assert.DoesNotContain("FALLBACK:", section.RelevanceDetail));
        Assert.Equal("Persona JSON parsed.", document.PersonaSections.Single(section => section.PersonaType == PersonaType.Developer).RelevantDevelopment);
    }

    [Fact]
    public async Task PersonaGeneration_MalformedJson_UsesExplicitlyMarkedFallback()
    {
        var (document, _) = await GeneratePersonaDocumentAsync("{ malformed json }");

        Assert.Equal(5, document.PersonaSections.Count);
        Assert.All(document.PersonaSections, section => Assert.StartsWith("FALLBACK:", section.RelevanceDetail));
    }

    [Fact]
    public async Task MockReportGeneration_WorksWithAndWithoutTrendEvidence_AndMarkdownContainsCitations()
    {
        var intelligence = new[] { Intelligence() };
        var trends = new[] { Trend("Agent", "Agent benchmark performance improved.") };
        var correlations = new[] { await new MockTrendCorrelationService().CorrelateAsync(intelligence[0], trends, CancellationToken.None) };
        var document = await new MockPersonaReportGenerator().GenerateAsync(intelligence, correlations, trends, CancellationToken.None);
        var markdown = new MarkdownReportRenderer().Render(document);

        Assert.Contains("# AI Technology Intelligence Report", markdown);
        Assert.Contains(intelligence[0].SourceUrl.AbsoluteUri, markdown);
        Assert.Contains(trends[0].SourceUrl.AbsoluteUri, markdown);
        Assert.Contains("CurrentOfficial", markdown);
        Assert.Contains("TrendResearch", markdown);

        var emptyTrendDocument = await new MockPersonaReportGenerator().GenerateAsync(intelligence, Array.Empty<TrendCorrelation>(), Array.Empty<TrendEvidence>(), CancellationToken.None);
        Assert.NotNull(emptyTrendDocument);
    }

    [Fact]
    public async Task ReportingPeriod_UsesIntelligenceItemPublishedYears()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Update.",
          "specificImpact": "Impact.",
          "trendImplication": "Trend.",
          "recommendedActions": [],
          "questionsToExplore": [],
          "watchItems": [],
          "relevance": "Mock"
        }
        """));
        var rawRepo = Substitute.For<IRawSourceRepository>();
        rawRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<RawSourceItem>());

        var generator = new LlmPersonaReportGenerator(llm, rawRepo, NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[]
        {
            Intelligence(summary: "Item one.", publishedAt: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            Intelligence(summary: "Item two.", publishedAt: new DateTimeOffset(2025, 6, 3, 0, 0, 0, TimeSpan.Zero))
        };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Current", "Trend", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, Array.Empty<TrendEvidence>(), CancellationToken.None);

        Assert.Equal("2025-2026", document.ReportingPeriod);
    }

    [Fact]
    public async Task ReportingPeriod_IgnoresMissingPublishedDates_WhenAtLeastOneExists()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Update.",
          "specificImpact": "Impact.",
          "trendImplication": "Trend.",
          "recommendedActions": [],
          "questionsToExplore": [],
          "watchItems": [],
          "relevance": "Mock"
        }
        """));
        var rawRepo = Substitute.For<IRawSourceRepository>();
        rawRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<RawSourceItem>());

        var generator = new LlmPersonaReportGenerator(llm, rawRepo, NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[]
        {
            Intelligence(summary: "Item one.", publishedAt: null, useDefaultPublishedAt: false),
            Intelligence(summary: "Item two.", publishedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero))
        };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Current", "Trend", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, Array.Empty<TrendEvidence>(), CancellationToken.None);

        Assert.Equal("2026", document.ReportingPeriod);
    }

    [Fact]
    public async Task ReportingPeriod_FallsBackToRawSourcePublishedAt_WhenMissingOnIntelligenceItems()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Update.",
          "specificImpact": "Impact.",
          "trendImplication": "Trend.",
          "recommendedActions": [],
          "questionsToExplore": [],
          "watchItems": [],
          "relevance": "Mock"
        }
        """));
        var rawRepo = Substitute.For<IRawSourceRepository>();
        var sourceId = Guid.NewGuid();
        var canonicalUrl = "https://example.com/raw";
        rawRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new RawSourceItem(
                sourceId,
                Guid.NewGuid(),
                "Raw",
                new Uri(canonicalUrl),
                new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero),
                DateTimeOffset.UtcNow,
                "content",
                new ContentHash(Guid.NewGuid().ToString("N")))
        });

        var generator = new LlmPersonaReportGenerator(llm, rawRepo, NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(summary: "Item one.", publishedAt: null, sourceItemId: sourceId, useDefaultPublishedAt: false, sourceUrl: new Uri(canonicalUrl)) };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Current", "Trend", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, Array.Empty<TrendEvidence>(), CancellationToken.None);

        Assert.Equal("2026", document.ReportingPeriod);
    }

    [Fact]
    public async Task ReportingPeriod_FallsBackToRawSourceFetchedAt_WhenPublishedDatesUnavailable()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Update.",
          "specificImpact": "Impact.",
          "trendImplication": "Trend.",
          "recommendedActions": [],
          "questionsToExplore": [],
          "watchItems": [],
          "relevance": "Mock"
        }
        """));
        var rawRepo = Substitute.For<IRawSourceRepository>();
        var sourceId = Guid.NewGuid();
        var canonicalUrl = "https://example.com/raw";
        rawRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new RawSourceItem(
                sourceId,
                Guid.NewGuid(),
                "Raw",
                new Uri(canonicalUrl),
                null,
                new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero),
                "content",
                new ContentHash(Guid.NewGuid().ToString("N")))
        });

        var generator = new LlmPersonaReportGenerator(llm, rawRepo, NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(summary: "Item one.", publishedAt: null, sourceItemId: sourceId, useDefaultPublishedAt: false, sourceUrl: new Uri(canonicalUrl)) };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Current", "Trend", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, Array.Empty<TrendEvidence>(), CancellationToken.None);

        Assert.Equal("2026", document.ReportingPeriod);
    }

    [Fact]
    public async Task ReportingPeriod_ReturnsUnknown_WhenNoCurrentOfficialDatesAvailable()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Update.",
          "specificImpact": "Impact.",
          "trendImplication": "Trend.",
          "recommendedActions": [],
          "questionsToExplore": [],
          "watchItems": [],
          "relevance": "Mock"
        }
        """));
        var rawRepo = Substitute.For<IRawSourceRepository>();
        rawRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new RawSourceItem(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Trend",
                new Uri("https://example.com/trend"),
                new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero),
                DateTimeOffset.UtcNow,
                "content",
                new ContentHash(Guid.NewGuid().ToString("N")),
                sourceClass: SourceClass.TrendResearch)
        });

        var generator = new LlmPersonaReportGenerator(llm, rawRepo, NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(summary: "Item one.", publishedAt: null, useDefaultPublishedAt: false) };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Current", "Trend", CorrelationRelationship.Supports, "Explanation", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, Array.Empty<TrendEvidence>(), CancellationToken.None);

        Assert.Equal("Unknown", document.ReportingPeriod);
    }

    [Fact]
    public void MarkdownRenderer_OmitsEmptyLists_ForLowRelevance()
    {
        var document = new ReportDocument(
            DateTimeOffset.UtcNow,
            "2026",
            "Summary",
            Array.Empty<ReportCurrentDevelopment>(),
            Array.Empty<ReportTrend>(),
            Array.Empty<TrendCorrelation>(),
            new[]
            {
                new PersonaReportSection(
                    PersonaType.QA,
                    PersonaRelevance.Low,
                    "QA",
                    "No relevant development.",
                    "Limited impact.",
                    "Unknown",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "Low")
            },
            Array.Empty<SourceReference>());

        var markdown = new MarkdownReportRenderer().Render(document);

        Assert.DoesNotContain("Recommended actions:", markdown);
        Assert.DoesNotContain("Questions to explore:", markdown);
        Assert.DoesNotContain("Watch items:", markdown);
    }

    [Fact]
    public void MarkdownRenderer_ShowsNoneAtThisTime_ForMediumRelevance()
    {
        var document = new ReportDocument(
            DateTimeOffset.UtcNow,
            "2026",
            "Summary",
            Array.Empty<ReportCurrentDevelopment>(),
            Array.Empty<ReportTrend>(),
            Array.Empty<TrendCorrelation>(),
            new[]
            {
                new PersonaReportSection(
                    PersonaType.Developer,
                    PersonaRelevance.Medium,
                    "Developer",
                    "No relevant development.",
                    "Limited impact.",
                    "Unknown",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "Medium")
            },
            Array.Empty<SourceReference>());

        var markdown = new MarkdownReportRenderer().Render(document);

        Assert.Contains("Recommended actions: None at this time.", markdown);
        Assert.Contains("Questions to explore: None at this time.", markdown);
        Assert.Contains("Watch items: None at this time.", markdown);
    }

    [Fact]
    public async Task PersonaGeneration_AssignsExpectedRelevance_ForDeveloperPlatformSignals()
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>()).Returns(new LLMResponse("""
        {
          "relevantDevelopment": "Developer platform announced new TypeScript tooling.",
          "specificImpact": "Developers will adopt the SDK and tooling changes.",
          "trendImplication": "Adoption likely accelerates.",
          "recommendedActions": ["Action one", "Action two"],
          "questionsToExplore": ["Question one"],
          "watchItems": ["Watch one"],
          "relevance": "Mock"
        }
        """));

        var generator = new LlmPersonaReportGenerator(llm, Substitute.For<IRawSourceRepository>(), NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(topic: "Developer platform", category: "Programming Language", product: "TypeScript SDK", summary: "Developer platform updates programming language tooling for AI-assisted development, developer experience, and coding tools.") };
        var trends = new[] { Trend("Developer productivity", "Developer platform adoption grows with AI tooling.") };
        var correlations = new[] { new TrendCorrelation(intelligence[0].Id, "Developer platform", "Adoption", CorrelationRelationship.Supports, "AI-assisted development improves productivity.", "Evidence basis", 0.7m, new[] { intelligence[0].SourceUrl, trends[0].SourceUrl }, false) };

        var document = await generator.GenerateAsync(intelligence, correlations, trends, CancellationToken.None);

        var developer = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Developer);
        var qa = document.PersonaSections.Single(section => section.PersonaType == PersonaType.QA);
        var pm = document.PersonaSections.Single(section => section.PersonaType == PersonaType.ProjectManager);
        var ba = document.PersonaSections.Single(section => section.PersonaType == PersonaType.BusinessAnalyst);
        var sales = document.PersonaSections.Single(section => section.PersonaType == PersonaType.Sales);

        Assert.True(developer.Relevance is PersonaRelevance.High or PersonaRelevance.Medium);
        Assert.True(qa.Relevance is PersonaRelevance.Low or PersonaRelevance.Medium or PersonaRelevance.NotRelevant);
        Assert.True(pm.Relevance is PersonaRelevance.Low or PersonaRelevance.Medium or PersonaRelevance.NotRelevant);
        Assert.True(ba.Relevance is PersonaRelevance.Low or PersonaRelevance.NotRelevant);
        Assert.True(sales.Relevance is PersonaRelevance.Low or PersonaRelevance.Medium or PersonaRelevance.High or PersonaRelevance.NotRelevant);
    }

    [Fact]
    public async Task CandidateSelector_PrefersIndependentSources_AsTieBreaker()
    {
        var intelligence = Intelligence(topic: "Developer platform", category: "AI Developer", product: "SDK", conceptTags: new[] { AIConceptTag.DeveloperPlatform });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Developer platform", "Same source evidence.", sourceUrl: intelligence.SourceUrl, conceptTags: new[] { AIConceptTag.DeveloperPlatform }, trendFamily: TrendFamily.OrganizationalAIAdoption),
            Trend("Developer platform", "Independent evidence.", sourceUrl: new Uri("https://trend.example.com/independent"), conceptTags: new[] { AIConceptTag.DeveloperPlatform }, trendFamily: TrendFamily.OrganizationalAIAdoption)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 1, CancellationToken.None);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal("https://trend.example.com/independent", candidate.SourceUrl.AbsoluteUri);
        Assert.Contains(selection.Diagnostics, diagnostic => diagnostic.Selected && diagnostic.MatchReason.Contains("topic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(diagnostic.SourceName));
    }

    [Fact]
    public async Task CandidateSelector_AllowsSameSourceWhenItHasHigherScore()
    {
        var intelligence = Intelligence(topic: "Developer platform", category: "AI Developer", product: "SDK", conceptTags: new[] { AIConceptTag.DeveloperPlatform });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Developer platform SDK", "Same source evidence.", sourceUrl: intelligence.SourceUrl, conceptTags: new[] { AIConceptTag.DeveloperPlatform }, trendFamily: TrendFamily.OrganizationalAIAdoption),
            Trend("Developer platform", "Independent evidence.", sourceUrl: new Uri("https://trend.example.com/independent"), conceptTags: new[] { AIConceptTag.DeveloperPlatform }, trendFamily: TrendFamily.OrganizationalAIAdoption)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 1, CancellationToken.None);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal(intelligence.SourceUrl, candidate.SourceUrl);
        Assert.Contains(selection.Diagnostics, diagnostic => diagnostic.SameSourceEvidence);
        Assert.Contains(selection.Diagnostics, diagnostic => diagnostic.Selected);
    }

    [Fact]
    public async Task CandidateSelector_RespectsMaxCandidateCount()
    {
        var intelligence = Intelligence(topic: "Agent evaluation", category: "AI Agent", product: "Agent SDK", conceptTags: new[] { AIConceptTag.AgenticAI, AIConceptTag.Evaluation });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Agent evaluation", "Agent benchmark performance improved.", sourceUrl: new Uri("https://trend.example.com/1"), conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentTaskPerformance),
            Trend("Agent evaluation", "Agent evaluation tooling updated.", sourceUrl: new Uri("https://trend.example.com/2"), conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentTaskPerformance),
            Trend("Agent evaluation", "Agent adoption continued.", sourceUrl: new Uri("https://trend.example.com/3"), conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentReliability),
            Trend("Agent evaluation", "Agent testing trends.", sourceUrl: new Uri("https://trend.example.com/4"), conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentReliability),
            Trend("Agent evaluation", "Agent workflow change.", sourceUrl: new Uri("https://trend.example.com/5"), conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentTaskPerformance)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 3, CancellationToken.None);

        Assert.Equal(3, selection.Candidates.Count);
    }

    [Fact]
    public async Task CandidateSelector_PrefersAgentFamilies_ForToolUseSignals()
    {
        var intelligence = Intelligence(
            topic: "Hosted agents",
            category: "AI Agent",
            product: "Agent SDK",
            summary: "Hosted agents gain new tool-use primitives.",
            conceptTags: new[] { AIConceptTag.AgenticAI, AIConceptTag.ToolUse });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Agent task performance", "Agent task completion rates improved.", conceptTags: new[] { AIConceptTag.AgenticAI, AIConceptTag.ToolUse }, trendFamily: TrendFamily.AgentTaskPerformance),
            Trend("Consumer value", "Consumer surplus continues climbing.", conceptTags: new[] { AIConceptTag.EnterpriseAdoption }, trendFamily: TrendFamily.ConsumerAIValue),
            Trend("Organization adoption", "Enterprise adoption for agents grows.", conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.OrganizationalAIAdoption)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 2, CancellationToken.None);

        Assert.Equal("Agent task performance", selection.Candidates.First().Topic);
        Assert.Contains(selection.Candidates, candidate => candidate.TrendFamily == TrendFamily.OrganizationalAIAdoption);
    }

    [Fact]
    public async Task CandidateSelector_PrioritizesSpeechAndMultimodalOverBenchmarks_WhenRelevant()
    {
        var intelligence = Intelligence(
            topic: "Speech LLM",
            category: "Speech",
            product: "Speech Model",
            summary: "Speech LLM adds multimodal reasoning.",
            conceptTags: new[] { AIConceptTag.SpeechVoice, AIConceptTag.MultimodalAI, AIConceptTag.ModelCapability });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Speech voice", "Speech model accuracy improves.", conceptTags: new[] { AIConceptTag.SpeechVoice }, trendFamily: TrendFamily.ModelPerformanceConvergence),
            Trend("Benchmark saturation", "Benchmarks are saturated.", conceptTags: new[] { AIConceptTag.Evaluation }, trendFamily: TrendFamily.BenchmarkSaturation),
            Trend("AI investment", "Capital inflows rise.", conceptTags: new[] { AIConceptTag.AIInvestment }, trendFamily: TrendFamily.CorporateAIInvestment)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 2, CancellationToken.None);

        Assert.Equal("Speech voice", selection.Candidates.First().Topic);
        Assert.DoesNotContain(selection.Candidates, candidate => candidate.Topic == "AI investment");
    }

    [Fact]
    public async Task CandidateSelector_WeightsFoundryRoundup_ToRelevantFamilies()
    {
        var intelligence = Intelligence(
            topic: "Foundry roundup",
            category: "Developer platform",
            product: "Foundry SDK",
            summary: "Foundry roundup highlights hosted agents, toolboxes, model router, voice live, local runtime, and SDK updates.",
            conceptTags: new[] { AIConceptTag.AgenticAI, AIConceptTag.ToolUse, AIConceptTag.ModelOperations, AIConceptTag.SpeechVoice, AIConceptTag.DeveloperPlatform });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Agent performance", "Agent tasks improve.", conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentTaskPerformance),
            Trend("Agent adoption", "Agent adoption grows.", conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.OrganizationalAIAdoption),
            Trend("Compute spend", "Compute infrastructure spend rises.", conceptTags: new[] { AIConceptTag.ModelOperations }, trendFamily: TrendFamily.ComputeInfrastructureSpend),
            Trend("Voice live", "Voice live usage expands.", conceptTags: new[] { AIConceptTag.SpeechVoice }, trendFamily: TrendFamily.ModelPerformanceConvergence),
            Trend("Open closed gap", "Open vs closed gap continues.", conceptTags: new[] { AIConceptTag.ModelCapability }, trendFamily: TrendFamily.OpenClosedModelGap)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 3, CancellationToken.None);

        Assert.DoesNotContain(selection.Candidates, candidate => candidate.TrendFamily == TrendFamily.OpenClosedModelGap);
        Assert.Contains(selection.Candidates, candidate => candidate.TrendFamily == TrendFamily.AgentTaskPerformance);
        Assert.Contains(selection.Candidates, candidate => candidate.TrendFamily == TrendFamily.OrganizationalAIAdoption);
        Assert.Contains(selection.Candidates, candidate => candidate.TrendFamily == TrendFamily.ComputeInfrastructureSpend);
    }

    [Fact]
    public async Task CandidateSelector_DoesNotLetRecencyOutweighRelevance()
    {
        var intelligence = Intelligence(
            topic: "Agent reliability",
            category: "AI Agent",
            product: "Agent SDK",
            summary: "Agent reliability tooling improved.",
            conceptTags: new[] { AIConceptTag.AgenticAI });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Agent reliability", "Agent reliability improved.", period: "2023", periodProvenance: TrendEvidencePeriodProvenance.PublicationDate, conceptTags: new[] { AIConceptTag.AgenticAI }, trendFamily: TrendFamily.AgentReliability),
            Trend("Consumer adoption", "Consumer adoption surged.", period: "2026", periodProvenance: TrendEvidencePeriodProvenance.SourceMetadata, conceptTags: new[] { AIConceptTag.EnterpriseAdoption }, trendFamily: TrendFamily.ConsumerAIAdoption)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 1, CancellationToken.None);

        Assert.Equal("Agent reliability", selection.Candidates.Single().Topic);
    }

    [Fact]
    public async Task CandidateSelector_FamilyPresenceWithoutCompatibility_DoesNotScore()
    {
        var intelligence = Intelligence(
            topic: "Speech LLM",
            category: "Speech",
            product: "Speech Model",
            summary: "Speech LLM updates.",
            conceptTags: new[] { AIConceptTag.SpeechVoice });
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Benchmark saturation", "Benchmarks saturated.", conceptTags: new[] { AIConceptTag.Evaluation }, trendFamily: TrendFamily.BenchmarkSaturation),
            Trend("Speech voice", "Speech improvements.", conceptTags: new[] { AIConceptTag.SpeechVoice }, trendFamily: TrendFamily.ModelPerformanceConvergence)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 2, CancellationToken.None);

        var benchmarkDiagnostic = Assert.Single(selection.Diagnostics.Where(diagnostic => diagnostic.Topic == "Benchmark saturation"));
        Assert.Equal(0, benchmarkDiagnostic.FamilyCompatibilityScore);
        Assert.Contains("Rejected", benchmarkDiagnostic.RejectionReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static IntelligenceItem Intelligence(
        string topic = "Agent evaluation",
        string category = "AI Agent",
        string product = "Agent SDK",
        string summary = "An agent evaluation capability changed.",
        IReadOnlyCollection<AIConceptTag>? conceptTags = null,
        DateTimeOffset? publishedAt = null,
        Guid? sourceItemId = null,
        bool useDefaultPublishedAt = true,
        Uri? sourceUrl = null) => new(
        Guid.NewGuid(),
        sourceItemId ?? Guid.NewGuid(),
        "Microsoft",
        topic,
        category,
        product,
        summary,
        new[] { "Evaluation" },
        new[] { "Unknown" },
        "Preview",
        SourceClass.CurrentOfficial,
        useDefaultPublishedAt ? publishedAt ?? DateTimeOffset.UtcNow : publishedAt,
        sourceUrl ?? new Uri("https://current.example.com"),
        conceptTags ?? new[] { AIConceptTag.Evaluation });

    private static TrendEvidence Trend(
        string topic,
        string finding,
        Uri? sourceUrl = null,
        IReadOnlyCollection<AIConceptTag>? conceptTags = null,
        TrendFamily trendFamily = TrendFamily.Unknown,
        string period = "2026",
        TrendEvidencePeriodProvenance periodProvenance = TrendEvidencePeriodProvenance.SourceContent,
        decimal confidence = 0.8m,
        string publicationName = "Trend Report")
    {
        var evidence = new TrendEvidence(
            Guid.NewGuid(),
            Guid.NewGuid(),
            topic,
            period,
            periodProvenance,
            finding,
            "Trend evidence summary.",
            confidence,
            sourceUrl ?? new Uri("https://trend.example.com"),
            "42%",
            publicationName,
            conceptTags);
        evidence.UpdateTrendFamily(trendFamily);
        return evidence;
    }

    private static async Task<(ReportDocument Document, ILLMClient Llm)> GeneratePersonaDocumentAsync(string response)
    {
        var llm = Substitute.For<ILLMClient>();
        llm.CompleteAsync(Arg.Any<LLMRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LLMResponse(response));
        var generator = new LlmPersonaReportGenerator(
            llm,
            Substitute.For<IRawSourceRepository>(),
            NullLogger<LlmPersonaReportGenerator>.Instance);
        var intelligence = new[] { Intelligence(summary: "A new SDK for agent development was announced.") };

        var document = await generator.GenerateAsync(
            intelligence,
            Array.Empty<TrendCorrelation>(),
            Array.Empty<TrendEvidence>(),
            CancellationToken.None);
        return (document, llm);
    }
}
