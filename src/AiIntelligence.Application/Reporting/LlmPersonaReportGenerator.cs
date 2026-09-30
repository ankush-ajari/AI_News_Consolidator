using System.Text.Json;
using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class LlmPersonaReportGenerator : IPersonaReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILLMClient _llmClient;
    private readonly ILogger<LlmPersonaReportGenerator> _logger;

    public LlmPersonaReportGenerator(ILLMClient llmClient, ILogger<LlmPersonaReportGenerator> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<ReportDocument> GenerateAsync(
        IReadOnlyCollection<IntelligenceItem> intelligenceItems,
        IReadOnlyCollection<TrendCorrelation> correlations,
        IReadOnlyCollection<TrendEvidence> trendEvidence,
        CancellationToken cancellationToken)
    {
        var personaTypes = new[] { PersonaType.Developer, PersonaType.QA, PersonaType.BusinessAnalyst, PersonaType.ProjectManager, PersonaType.Sales };
        var relevanceAssessments = personaTypes.ToDictionary(
            personaType => personaType,
            personaType => DetermineRelevance(personaType, intelligenceItems, correlations, trendEvidence));

        if (relevanceAssessments.Values.All(assessment => assessment.Relevance == PersonaRelevance.NotRelevant))
        {
            _logger.LogWarning("All persona sections classified as NotRelevant. Review relevance gating logic or source coverage.");
            foreach (var assessment in relevanceAssessments)
            {
                _logger.LogWarning("Persona relevance assessment for {PersonaType}: {Relevance}. Reasoning: {Reasoning}",
                    assessment.Key,
                    assessment.Value.Relevance,
                    assessment.Value.Reasoning);
            }
        }

        var personaSections = new List<PersonaReportSection>();
        foreach (var personaType in personaTypes)
        {
            personaSections.Add(await GeneratePersonaSectionAsync(personaType, relevanceAssessments[personaType], intelligenceItems, correlations, trendEvidence, cancellationToken).ConfigureAwait(false));
        }

        return new ReportDocument(
            DateTimeOffset.UtcNow,
            DetermineReportingPeriod(intelligenceItems, trendEvidence),
            BuildOverallSummary(intelligenceItems, correlations),
            intelligenceItems.Select(item => new ReportCurrentDevelopment(
                item.Topic,
                item.Summary,
                string.Join(" / ", new[] { item.Vendor, item.ProductOrFramework }.Where(value => !string.IsNullOrWhiteSpace(value))),
                $"Relevant to {item.Category}; release stage: {item.ReleaseStage}.",
                item.SourceUrl)).ToArray(),
            trendEvidence.Select(trend => new ReportTrend(
                trend.Topic,
                trend.EvidenceSummary,
                trend.Period,
                trend.PeriodProvenance,
                trend.Confidence,
                new[] { trend.SourceUrl })).ToArray(),
            correlations,
            personaSections,
            BuildSourceReferences(intelligenceItems, trendEvidence));
    }

    public static string BuildPersonaPrompt(PersonaType personaType, PersonaRelevance relevance, IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendCorrelation> correlations, IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        return $$"""
        Generate one persona-specific section for: {{personaType}}.
        Use only supplied evidence. Do not invent facts. Do not represent TrendResearch as official vendor announcements.

        You are not summarizing the source.
        You are interpreting the supplied evidence specifically for this persona.

        Do not copy the general development summary verbatim.

        Do not use generic recommendations such as:
        'Review cited sources and validate applicability before action.'

        If the evidence has limited impact for this persona,
        state that explicitly rather than inventing relevance.

        Persona-specific focus:
        {{GetPersonaInstructions(personaType)}}

        Relevance guidance:
        - High: generate full persona analysis with concrete impacts and multiple actions.
        - Medium: generate useful but concise analysis, limit actions to the most actionable items.
        - Low: generate a short section explaining limited relevance; avoid multiple recommendations.
        - NotRelevant: explicitly say the evidence does not indicate a material impact; do not invent actions.
        Relevance level: {{relevance}}

        Return JSON with fields:
        relevantDevelopment: string
        specificImpact: string
        trendImplication: string
        recommendedActions: string[]
        questionsToExplore: string[]
        watchItems: string[]
        relevance: string

        Current developments:
        {{JsonSerializer.Serialize(intelligenceItems.Select(i => new { i.Topic, i.Category, i.ProductOrFramework, i.Summary, SourceUrl = i.SourceUrl }))}}

        Correlations:
        {{JsonSerializer.Serialize(correlations.Select(c => new { c.Relationship, c.CurrentDevelopment, c.RelatedTrend, c.Explanation, c.SupportingSourceUrls }))}}

        Trend evidence:
        {{JsonSerializer.Serialize(trendEvidence.Select(t => new { t.Topic, t.Period, t.Finding, t.EvidenceSummary, t.Confidence, SourceUrl = t.SourceUrl }))}}
        """;
    }

    private static string GetPersonaInstructions(PersonaType personaType) => personaType switch
    {
        PersonaType.Developer => "Focus on APIs, SDKs, frameworks, architecture, integration patterns, model/tool capabilities, implementation implications, migration implications, and skills developers should learn.",
        PersonaType.QA => "Focus on test strategy, non-deterministic behavior, evaluation, regression, reliability, observability, safety, test automation, and new QA skills/tools.",
        PersonaType.BusinessAnalyst => "Focus on business use cases, workflow/process changes, requirement implications, acceptance criteria, capability constraints, business/user impact, and stakeholder questions.",
        PersonaType.ProjectManager => "Focus on delivery planning, dependencies, team capability, schedule impact, adoption readiness, risks, governance, and pilot/experimentation needs.",
        PersonaType.Sales => "Focus on customer relevance, customer discussion themes, opportunities, differentiators, enterprise adoption signals, limitations/caveats, and what must not be oversold.",
        _ => "Focus on role-specific implications."
    };

    private async Task<PersonaReportSection> GeneratePersonaSectionAsync(
        PersonaType personaType,
        PersonaRelevanceAssessment relevanceAssessment,
        IReadOnlyCollection<IntelligenceItem> intelligenceItems,
        IReadOnlyCollection<TrendCorrelation> correlations,
        IReadOnlyCollection<TrendEvidence> trendEvidence,
        CancellationToken cancellationToken)
    {
        var response = await _llmClient.CompleteAsync(
            new LLMRequest(
                "Return persona guidance as JSON. Do not include unsupported claims. Do not copy generic summaries verbatim.",
                BuildPersonaPrompt(personaType, relevanceAssessment.Relevance, intelligenceItems, correlations, trendEvidence),
                null,
                null),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var dto = JsonSerializer.Deserialize<PersonaSectionDto>(response.Content, JsonOptions);
            return ApplyRelevanceGuardrails(new PersonaReportSection(
                personaType,
                relevanceAssessment.Relevance,
                personaType.ToString(),
                OrFallback(dto?.RelevantDevelopment, "No specific persona-relevant development identified."),
                OrFallback(dto?.SpecificImpact, "Limited persona-specific impact identified from supplied evidence."),
                OrFallback(dto?.TrendImplication, "Unknown."),
                Normalize(dto?.RecommendedActions),
                Normalize(dto?.QuestionsToExplore),
                Normalize(dto?.WatchItems),
                OrFallback(dto?.Relevance, "Unknown")),
                relevanceAssessment.Relevance,
                personaType);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Malformed persona section JSON for {PersonaType}; using deterministic fallback.", personaType);
            return BuildFallbackPersonaSection(personaType, relevanceAssessment.Relevance, intelligenceItems, correlations);
        }
    }

    private static PersonaReportSection BuildFallbackPersonaSection(PersonaType personaType, PersonaRelevance relevance, IReadOnlyCollection<IntelligenceItem> items, IReadOnlyCollection<TrendCorrelation> correlations)
    {
        var first = items.FirstOrDefault();
        var firstCorrelation = correlations.FirstOrDefault();
        return ApplyRelevanceGuardrails(new PersonaReportSection(
            personaType,
            relevance,
            personaType.ToString(),
            first?.Topic ?? "No current development available.",
            personaType switch
            {
                PersonaType.Developer => "Potential impact on implementation choices, integration patterns, APIs, SDKs, and technical skills.",
                PersonaType.QA => "Potential impact on evaluation strategy, nondeterministic testing, reliability, safety, and observability.",
                PersonaType.BusinessAnalyst => "Potential impact on requirements, acceptance criteria, workflows, and stakeholder questions.",
                PersonaType.ProjectManager => "Potential impact on delivery plans, dependencies, risk management, governance, and pilot readiness.",
                PersonaType.Sales => "Potential impact on customer conversations, differentiation, adoption signals, and caveats that should not be oversold.",
                _ => "Assess role-specific implications."
            },
            firstCorrelation?.RelatedTrend ?? "Unknown",
            personaType switch
            {
                PersonaType.Developer => new[] { "Evaluate API and SDK impact", "Prototype integration pattern", "Identify skills to learn" },
                PersonaType.QA => new[] { "Define evaluation cases", "Plan regression coverage", "Review observability needs" },
                PersonaType.BusinessAnalyst => new[] { "Refine use cases", "Update acceptance criteria", "Clarify workflow impact" },
                PersonaType.ProjectManager => new[] { "Assess delivery risk", "Plan pilot", "Identify dependencies" },
                PersonaType.Sales => new[] { "Prepare customer discussion themes", "Document caveats", "Align value messaging with evidence" },
                _ => new[] { "Review role impact" }
            },
            new[] { "What evidence is strong enough for action?", "What constraints remain?" },
            new[] { firstCorrelation?.Relationship.ToString() ?? "InsufficientEvidence" },
            first is null ? "Limited" : "Potentially relevant"),
            relevance,
            personaType);
    }

    private static string OrFallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static IReadOnlyCollection<string> Normalize(IReadOnlyCollection<string>? values)
    {
        return values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray()
            ?? Array.Empty<string>();
    }

    private static string BuildOverallSummary(IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendCorrelation> correlations)
    {
        return $"Processed {intelligenceItems.Count} current developments and created {correlations.Count} current-development-to-trend correlations.";
    }

    private static string DetermineReportingPeriod(IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        var years = intelligenceItems.Select(item => item.PublishedAt?.Year).Where(year => year.HasValue).Select(year => year!.Value)
            .Concat(trendEvidence.Select(trend => trend.Period).Select(period => int.TryParse(period, out var year) ? year : (int?)null).Where(year => year.HasValue).Select(year => year!.Value))
            .Distinct()
            .OrderBy(year => year)
            .ToArray();
        return years.Length == 0 ? "Unknown" : string.Join(", ", years);
    }

    private static IReadOnlyCollection<SourceReference> BuildSourceReferences(IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        return intelligenceItems.Select(item => new SourceReference($"CurrentOfficial: {item.Topic}", item.SourceUrl, "CurrentOfficial"))
            .Concat(trendEvidence.Select(trend => new SourceReference($"TrendResearch: {trend.Topic}", trend.SourceUrl, "TrendResearch")))
            .GroupBy(reference => reference.Url.AbsoluteUri)
            .Select(group => group.First())
            .ToArray();
    }

    private sealed record PersonaSectionDto(
        string? RelevantDevelopment,
        string? SpecificImpact,
        string? TrendImplication,
        IReadOnlyCollection<string>? RecommendedActions,
        IReadOnlyCollection<string>? QuestionsToExplore,
        IReadOnlyCollection<string>? WatchItems,
        string? Relevance);

    private sealed record PersonaRelevanceAssessment(PersonaRelevance Relevance, string Reasoning);

    private sealed record SignalScore(int Score, string Reasoning);

    private static PersonaRelevanceAssessment DetermineRelevance(
        PersonaType personaType,
        IReadOnlyCollection<IntelligenceItem> intelligenceItems,
        IReadOnlyCollection<TrendCorrelation> correlations,
        IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        var evidenceText = BuildEvidenceText(intelligenceItems, correlations, trendEvidence);
        var developerSignals = ScoreSignals(evidenceText, new[]
        {
            "programming language", "language", "developer platform", "framework", "sdk", "api",
            "coding tool", "tooling", "architecture", "integration", "ai-assisted", "copilot",
            "implementation", "developer experience", "dx", "platform"
        });

        var qaSignals = ScoreSignals(evidenceText, new[]
        {
            "test", "testability", "reliability", "evaluation", "automation", "observability",
            "quality", "risk", "regression", "safety", "lifecycle"
        });

        var baSignals = ScoreSignals(evidenceText, new[]
        {
            "workflow", "requirement", "capability", "process", "automation", "adoption",
            "business", "stakeholder", "user"
        });

        var pmSignals = ScoreSignals(evidenceText, new[]
        {
            "delivery", "productivity", "skills", "adoption", "dependency", "risk",
            "governance", "planning", "schedule", "readiness"
        });

        var salesSignals = ScoreSignals(evidenceText, new[]
        {
            "enterprise", "customer", "positioning", "market", "use case", "adoption",
            "platform", "product", "demand", "signal"
        });

        var developerInfluence = developerSignals.Score >= 2;
        var adoptionInfluence = evidenceText.Contains("adoption", StringComparison.OrdinalIgnoreCase)
            || evidenceText.Contains("productivity", StringComparison.OrdinalIgnoreCase)
            || evidenceText.Contains("platform", StringComparison.OrdinalIgnoreCase);

        var relevanceAssessment = personaType switch
        {
            PersonaType.Developer => BuildAssessment(ToRelevance(developerSignals, highThreshold: 3, mediumThreshold: 2), developerSignals.Reasoning),
            PersonaType.QA => BuildAssessment(
                ToRelevance(qaSignals, highThreshold: 3, mediumThreshold: 2),
                qaSignals.Reasoning,
                fallback: developerInfluence ? (PersonaRelevance.Low, "Inferred secondary QA impact from developer platform changes.") : null),
            PersonaType.BusinessAnalyst => BuildAssessment(
                ToRelevance(baSignals, highThreshold: 3, mediumThreshold: 2),
                baSignals.Reasoning,
                fallback: adoptionInfluence ? (PersonaRelevance.Low, "Inferred workflow implications from adoption signals.") : null),
            PersonaType.ProjectManager => BuildAssessment(
                ToRelevance(pmSignals, highThreshold: 3, mediumThreshold: 2),
                pmSignals.Reasoning,
                fallback: developerInfluence || adoptionInfluence ? (PersonaRelevance.Low, "Inferred project delivery impact from developer platform signals.") : null),
            PersonaType.Sales => BuildAssessment(
                ToRelevance(salesSignals, highThreshold: 3, mediumThreshold: 2),
                salesSignals.Reasoning,
                fallback: adoptionInfluence ? (PersonaRelevance.Low, "Inferred market relevance from platform adoption signals.") : null),
            _ => new PersonaRelevanceAssessment(PersonaRelevance.NotRelevant, "No evidence signals matched.")
        };

        return relevanceAssessment;
    }

    private static string BuildEvidenceText(
        IReadOnlyCollection<IntelligenceItem> intelligenceItems,
        IReadOnlyCollection<TrendCorrelation> correlations,
        IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        var intelligenceText = string.Join(' ', intelligenceItems.Select(item => $"{item.Topic} {item.Category} {item.ProductOrFramework} {item.Summary}"));
        var trendText = string.Join(' ', trendEvidence.Select(trend => $"{trend.Topic} {trend.Finding} {trend.EvidenceSummary}"));
        var correlationText = string.Join(' ', correlations.Select(correlation => $"{correlation.CurrentDevelopment} {correlation.RelatedTrend} {correlation.Explanation}"));
        return string.Join(' ', new[] { intelligenceText, trendText, correlationText }).ToLowerInvariant();
    }

    private static SignalScore ScoreSignals(string evidenceText, IEnumerable<string> signals)
    {
        var matched = signals.Where(signal => evidenceText.Contains(signal, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        var score = matched.Length;
        var reasoning = matched.Length == 0
            ? "No supporting signals found in supplied evidence."
            : $"Signals matched: {string.Join(", ", matched)}.";
        return new SignalScore(score, reasoning);
    }

    private static PersonaRelevance ToRelevance(SignalScore score, int highThreshold, int mediumThreshold)
    {
        if (score.Score >= highThreshold)
        {
            return PersonaRelevance.High;
        }

        if (score.Score >= mediumThreshold)
        {
            return PersonaRelevance.Medium;
        }

        if (score.Score == 1)
        {
            return PersonaRelevance.Low;
        }

        return PersonaRelevance.NotRelevant;
    }

    private static PersonaRelevanceAssessment BuildAssessment(PersonaRelevance relevance, string reasoning, (PersonaRelevance relevance, string reasoning)? fallback = null)
    {
        if (relevance == PersonaRelevance.NotRelevant && fallback.HasValue)
        {
            return new PersonaRelevanceAssessment(fallback.Value.relevance, fallback.Value.reasoning);
        }

        return new PersonaRelevanceAssessment(relevance, reasoning);
    }

    private static PersonaReportSection ApplyRelevanceGuardrails(PersonaReportSection section, PersonaRelevance relevance, PersonaType personaType)
    {
        if (relevance == PersonaRelevance.NotRelevant)
        {
            return section with
            {
                RelevantDevelopment = "No material persona-specific development identified.",
                SpecificImpact = "The available evidence does not indicate a material impact for this persona.",
                TrendImplication = "Insufficient persona-relevant evidence to assess trend impact.",
                RecommendedActions = Array.Empty<string>(),
                QuestionsToExplore = Array.Empty<string>(),
                WatchItems = Array.Empty<string>(),
                RelevanceDetail = "Not relevant based on supplied evidence."
            };
        }

        if (relevance == PersonaRelevance.Low)
        {
            return section with
            {
                RecommendedActions = section.RecommendedActions.Take(1).ToArray(),
                QuestionsToExplore = section.QuestionsToExplore.Take(1).ToArray(),
                WatchItems = section.WatchItems.Take(1).ToArray(),
                SpecificImpact = section.SpecificImpact.Length > 220
                    ? section.SpecificImpact[..220].TrimEnd() + "…"
                    : section.SpecificImpact,
                RelevanceDetail = string.IsNullOrWhiteSpace(section.RelevanceDetail)
                    ? "Limited relevance for this persona."
                    : section.RelevanceDetail
            };
        }

        if (relevance == PersonaRelevance.Medium)
        {
            return section with
            {
                RecommendedActions = section.RecommendedActions.Take(3).ToArray(),
                QuestionsToExplore = section.QuestionsToExplore.Take(3).ToArray(),
                WatchItems = section.WatchItems.Take(3).ToArray(),
                RelevanceDetail = string.IsNullOrWhiteSpace(section.RelevanceDetail)
                    ? "Moderate relevance for this persona."
                    : section.RelevanceDetail
            };
        }

        return section with
        {
            RelevanceDetail = string.IsNullOrWhiteSpace(section.RelevanceDetail)
                ? "High relevance for this persona."
                : section.RelevanceDetail
        };
    }
}
