using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Application.Tests;

public sealed class ReportingTests
{
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

        var generator = new LlmPersonaReportGenerator(llm, NullLogger<LlmPersonaReportGenerator>.Instance);
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

        var generator = new LlmPersonaReportGenerator(llm, NullLogger<LlmPersonaReportGenerator>.Instance);
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

        var generator = new LlmPersonaReportGenerator(llm, NullLogger<LlmPersonaReportGenerator>.Instance);
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
    public async Task CandidateSelector_PrefersIndependentSources_AndExcludesSameSourceWhenAvailable()
    {
        var intelligence = Intelligence(topic: "Developer platform", category: "AI Developer", product: "SDK");
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Developer platform", "Same source evidence.", sourceUrl: intelligence.SourceUrl),
            Trend("Developer platform adoption", "Independent evidence.", sourceUrl: new Uri("https://trend.example.com/independent"))
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 3, CancellationToken.None);

        Assert.Contains(selection.Candidates, candidate => candidate.SourceUrl.AbsoluteUri == "https://trend.example.com/independent");
        Assert.DoesNotContain(selection.Candidates, candidate => candidate.SourceUrl == intelligence.SourceUrl);
        Assert.Contains(selection.Diagnostics, diagnostic => diagnostic.Selected && diagnostic.MatchReason.Contains("topic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(diagnostic.SourceName));
    }

    [Fact]
    public async Task CandidateSelector_AllowsSameSourceOnlyWhenNoIndependentCandidates()
    {
        var intelligence = Intelligence(topic: "Developer platform", category: "AI Developer", product: "SDK");
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Developer platform", "Same source evidence.", sourceUrl: intelligence.SourceUrl)
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 3, CancellationToken.None);

        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal(intelligence.SourceUrl, candidate.SourceUrl);
        Assert.Contains(selection.Diagnostics, diagnostic => diagnostic.SameSourceEvidence);
        Assert.Contains(selection.Diagnostics, diagnostic => diagnostic.Selected);
    }

    [Fact]
    public async Task CandidateSelector_RespectsMaxCandidateCount()
    {
        var intelligence = Intelligence(topic: "Agent evaluation", category: "AI Agent", product: "Agent SDK");
        var repo = Substitute.For<ITrendEvidenceRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Trend("Agent evaluation", "Agent benchmark performance improved.", sourceUrl: new Uri("https://trend.example.com/1")),
            Trend("Agent evaluation", "Agent evaluation tooling updated.", sourceUrl: new Uri("https://trend.example.com/2")),
            Trend("Agent evaluation", "Agent adoption continued.", sourceUrl: new Uri("https://trend.example.com/3")),
            Trend("Agent evaluation", "Agent testing trends.", sourceUrl: new Uri("https://trend.example.com/4")),
            Trend("Agent evaluation", "Agent workflow change.", sourceUrl: new Uri("https://trend.example.com/5"))
        });
        var selector = new TrendCandidateSelector(repo, NullLogger<TrendCandidateSelector>.Instance);

        var selection = await selector.SelectCandidatesAsync(intelligence, maxCandidates: 3, CancellationToken.None);

        Assert.Equal(3, selection.Candidates.Count);
    }

    private static IntelligenceItem Intelligence(string topic = "Agent evaluation", string category = "AI Agent", string product = "Agent SDK", string summary = "An agent evaluation capability changed.") => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Microsoft",
        topic,
        category,
        product,
        summary,
        new[] { "Evaluation" },
        new[] { "Unknown" },
        "Preview",
        SourceClass.CurrentOfficial,
        DateTimeOffset.UtcNow,
        new Uri("https://current.example.com"));

    private static TrendEvidence Trend(string topic, string finding, Uri? sourceUrl = null) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        topic,
        "2026",
        TrendEvidencePeriodProvenance.SourceContent,
        finding,
        "Trend evidence summary.",
        0.8m,
        sourceUrl ?? new Uri("https://trend.example.com"),
        "42%",
        "Trend Report");
}
