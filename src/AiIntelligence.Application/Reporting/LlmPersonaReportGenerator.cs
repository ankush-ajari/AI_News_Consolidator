using System.Text.Json;
using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class LlmPersonaReportGenerator : IPersonaReportGenerator
{
        public const string PersonaSectionJsonSchemaName = "persona_report_section";

        private const string PersonaSectionJsonSchema = """
        {
            "type": "object",
            "properties": {
                "relevantDevelopment": { "type": "string" },
                "specificImpact": { "type": "string" },
                "trendImplication": { "type": "string" },
                "recommendedActions": { "type": "array", "items": { "type": "string" } },
                "questionsToExplore": { "type": "array", "items": { "type": "string" } },
                "watchItems": { "type": "array", "items": { "type": "string" } },
                "relevance": { "type": "string" }
            },
            "required": [
                "relevantDevelopment",
                "specificImpact",
                "trendImplication",
                "recommendedActions",
                "questionsToExplore",
                "watchItems",
                "relevance"
            ],
            "additionalProperties": false
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILLMClient _llmClient;
    private readonly IRawSourceRepository _rawSourceRepository;
    private readonly ILogger<LlmPersonaReportGenerator> _logger;

    public LlmPersonaReportGenerator(
        ILLMClient llmClient,
        IRawSourceRepository rawSourceRepository,
        ILogger<LlmPersonaReportGenerator> logger)
    {
        _llmClient = llmClient;
        _rawSourceRepository = rawSourceRepository;
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

        // Determine the set of RawSourceItem ids we actually need for persona/reporting
        // lookups. Avoid loading RawContent/EnrichedContent for the entire table.
        var requiredRawIds = new HashSet<Guid>();
        if (intelligenceItems is not null)
        {
            foreach (var item in intelligenceItems)
            {
                if (item.SourceItemId != Guid.Empty)
                {
                    requiredRawIds.Add(item.SourceItemId);
                }
            }
        }

        if (trendEvidence is not null)
        {
            foreach (var t in trendEvidence)
            {
                if (t.SourceItemId != Guid.Empty)
                {
                    requiredRawIds.Add(t.SourceItemId);
                }
            }
        }

        IReadOnlyCollection<RawSourceItemSummary> rawSummaries;
        if (requiredRawIds.Count == 0)
        {
            rawSummaries = Array.Empty<RawSourceItemSummary>();
        }
        else
        {
                rawSummaries = await _rawSourceRepository.GetByIdsAsync(requiredRawIds, cancellationToken).ConfigureAwait(false)
                    ?? Array.Empty<RawSourceItemSummary>();
        }

        var currentOfficial = rawSummaries
            .Where(item => item.SourceClass == SourceClass.CurrentOfficial)
            .ToArray();

        var rawPublishedLookup = currentOfficial
            .ToDictionary(item => item.Id, item => item.PublishedAt);

        var rawPublishedByCanonicalUrl = currentOfficial
            .Where(item => !string.IsNullOrWhiteSpace(item.CanonicalUrl))
            .GroupBy(item => item.CanonicalUrl)
            .ToDictionary(group => group.Key, group => group.Select(item => item.PublishedAt).DefaultIfEmpty().Max());

        var rawFetchedLookup = currentOfficial
            .ToDictionary(item => item.Id, item => item.FetchedAt ?? DateTimeOffset.MinValue);

        var rawFetchedByCanonicalUrl = currentOfficial
            .Where(item => !string.IsNullOrWhiteSpace(item.CanonicalUrl))
            .GroupBy(item => item.CanonicalUrl)
            .ToDictionary(group => group.Key, group => group.Max(item => item.FetchedAt ?? DateTimeOffset.MinValue));

        var rawSourcesById = rawSummaries.ToDictionary(item => item.Id, item => item);

        var trendDeduplication = FilterCanonicalTrendEvidence(trendEvidence, rawSourcesById);
        var canonicalTrends = trendDeduplication.CanonicalTrends;

        _logger.LogInformation("Trend de-duplication: {BeforeCount} -> {AfterCount}.", trendEvidence.Count, canonicalTrends.Count);
        foreach (var group in trendDeduplication.SuppressedGroups)
        {
            _logger.LogInformation(
                "Trend de-duplication suppressed: Canonical={CanonicalTopic}; Period={CanonicalPeriod}; Family={TrendFamily}; Suppressed={Suppressed}",
                group.CanonicalTopic,
                group.CanonicalPeriod,
                group.TrendFamily,
                string.Join(" | ", group.SuppressedTopics));
        }

        return new ReportDocument(
            DateTimeOffset.UtcNow,
            DetermineReportingPeriod(intelligenceItems, rawPublishedLookup, rawPublishedByCanonicalUrl, rawFetchedLookup, rawFetchedByCanonicalUrl),
            BuildOverallSummary(intelligenceItems, correlations),
            intelligenceItems.Select(item => new ReportCurrentDevelopment(
                item.Topic,
                item.Summary,
                string.Join(" / ", new[] { item.Vendor, item.ProductOrFramework }.Where(value => !string.IsNullOrWhiteSpace(value))),
                $"Relevant to {item.Category}; release stage: {item.ReleaseStage}.",
                item.SourceUrl)).ToArray(),
            canonicalTrends.Select(trend => new ReportTrend(
                trend.Topic,
                trend.EvidenceSummary,
                trend.Period,
                trend.PeriodProvenance,
                trend.Confidence,
                new[] { trend.SourceUrl })).ToArray(),
            correlations,
            personaSections,
            BuildSourceReferences(intelligenceItems, canonicalTrends));
    }

    public static string BuildPersonaPrompt(PersonaType personaType, PersonaRelevance relevance, IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendCorrelation> correlations, IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        return $$"""
        Generate one persona-specific section for: {{personaType}}.
        Return exactly one JSON object matching the required schema. Do not include markdown fences, headings, commentary, or any text before or after the JSON object.
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
        - If a correlation relationship is InsufficientEvidence, treat the trend as broader context only.
          Do not imply the current development confirms, supports, or is driven by that trend.
          Base recommendations on the current development itself, and explicitly distinguish broader context from evidence-supported implication.
        Relevance level: {{relevance}}

        JSON fields:
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
                "Return exactly one JSON object matching the supplied response schema. Do not include markdown fences, prose, or unsupported claims. Do not copy generic summaries verbatim.",
                BuildPersonaPrompt(personaType, relevanceAssessment.Relevance, intelligenceItems, correlations, trendEvidence),
                PersonaSectionJsonSchemaName,
                PersonaSectionJsonSchema),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var json = ExtractPersonaJsonObject(response.Content);
            var dto = JsonSerializer.Deserialize<PersonaSectionDto>(json, JsonOptions)
                ?? throw new JsonException("Persona response JSON was null.");
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
            _logger.LogError(
                exception,
                "Persona response could not be parsed for {PersonaType}; deterministic fallback used. RawResponseSample: {RawResponseSample}",
                personaType,
                SanitizeResponseForLog(response.Content));
            return BuildFallbackPersonaSection(personaType, relevanceAssessment.Relevance, intelligenceItems, correlations) with
            {
                RelevanceDetail = "FALLBACK: Real-model persona response was invalid; deterministic fallback used."
            };
        }
    }

    private static string ExtractPersonaJsonObject(string response)
    {
        var content = response.Trim();
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = content.IndexOf('\n');
            if (firstLineEnd < 0 || !content.EndsWith("```", StringComparison.Ordinal))
            {
                throw new JsonException("Persona response contains an incomplete markdown fence.");
            }

            content = content[(firstLineEnd + 1)..^3].Trim();
        }

        if (content.StartsWith('{') && content.EndsWith('}'))
        {
            return content;
        }

        var start = content.IndexOf('{');
        if (start < 0)
        {
            throw new JsonException("Persona response did not contain a JSON object.");
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        var end = -1;
        for (var index = start; index < content.Length; index++)
        {
            var character = content[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                end = index;
                break;
            }
        }

        var prefix = content[..start];
        var suffix = end >= 0 ? content[(end + 1)..] : string.Empty;
        if (end < 0
            || prefix.Contains('{')
            || prefix.Contains('}')
            || suffix.Contains('{')
            || suffix.Contains('}'))
        {
            throw new JsonException("Persona response did not contain one unambiguous JSON object.");
        }

        return content[start..(end + 1)];
    }

    private static string SanitizeResponseForLog(string response)
    {
        var sanitized = new string(response
            .Select(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t' ? '?' : character)
            .ToArray())
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        return sanitized.Length <= 1000 ? sanitized : sanitized[..1000] + "...";
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
            firstCorrelation?.Relationship == CorrelationRelationship.InsufficientEvidence
                ? "Trend evidence provides broader context only; no direct relationship is established."
                : (first is null ? "Limited" : "Potentially relevant")),
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

    private static string DetermineReportingPeriod(
        IReadOnlyCollection<IntelligenceItem> intelligenceItems,
        IReadOnlyDictionary<Guid, DateTimeOffset?> rawPublishedLookup,
        IReadOnlyDictionary<string, DateTimeOffset?> rawPublishedByCanonicalUrl,
        IReadOnlyDictionary<Guid, DateTimeOffset> rawFetchedLookup,
        IReadOnlyDictionary<string, DateTimeOffset> rawFetchedByCanonicalUrl)
    {
        var years = intelligenceItems
            .Select(item => ResolvePublishedAt(item, rawPublishedLookup, rawPublishedByCanonicalUrl))
            .Where(published => published.HasValue)
            .Select(published => published!.Value.Year)
            .Distinct()
            .OrderBy(year => year)
            .ToArray();

        if (years.Length == 0)
        {
            years = intelligenceItems
                .Select(item => ResolveFetchedAt(item, rawFetchedLookup, rawFetchedByCanonicalUrl))
                .Where(fetched => fetched.HasValue)
                .Select(fetched => fetched!.Value.Year)
                .Distinct()
                .OrderBy(year => year)
                .ToArray();
        }

        if (years.Length == 0)
        {
            return "Unknown";
        }

        return years.Length == 1
            ? years[0].ToString()
            : $"{years.First()}-{years.Last()}";
    }

    private static DateTimeOffset? ResolvePublishedAt(
        IntelligenceItem item,
        IReadOnlyDictionary<Guid, DateTimeOffset?> rawPublishedLookup,
        IReadOnlyDictionary<string, DateTimeOffset?> rawPublishedByCanonicalUrl)
    {
        if (item.PublishedAt.HasValue)
        {
            return item.PublishedAt;
        }

        if (rawPublishedLookup.TryGetValue(item.SourceItemId, out var publishedAt))
        {
            return publishedAt;
        }

        var canonicalUrl = CanonicalUrlNormalizer.Normalize(item.SourceUrl);
        return rawPublishedByCanonicalUrl.TryGetValue(canonicalUrl, out var byUrl)
            ? byUrl
            : null;
    }

    private static DateTimeOffset? ResolveFetchedAt(
        IntelligenceItem item,
        IReadOnlyDictionary<Guid, DateTimeOffset> rawFetchedLookup,
        IReadOnlyDictionary<string, DateTimeOffset> rawFetchedByCanonicalUrl)
    {
        if (rawFetchedLookup.TryGetValue(item.SourceItemId, out var fetchedAt))
        {
            return fetchedAt;
        }

        var canonicalUrl = CanonicalUrlNormalizer.Normalize(item.SourceUrl);
        return rawFetchedByCanonicalUrl.TryGetValue(canonicalUrl, out var byUrl)
            ? byUrl
            : null;
    }

    private static IReadOnlyCollection<SourceReference> BuildSourceReferences(IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendEvidence> trendEvidence)
    {
        return intelligenceItems.Select(item => new SourceReference($"CurrentOfficial: {item.Topic}", item.SourceUrl, "CurrentOfficial"))
            .Concat(trendEvidence.Select(trend => new SourceReference($"TrendResearch: {trend.Topic}", trend.SourceUrl, "TrendResearch")))
            .GroupBy(reference => reference.Url.AbsoluteUri)
            .Select(group => group.First())
            .ToArray();
    }

    private static TrendDeduplicationResult FilterCanonicalTrendEvidence(
        IReadOnlyCollection<TrendEvidence> evidence,
        IReadOnlyDictionary<Guid, RawSourceItemSummary> rawSourcesById)
    {
        if (evidence.Count == 0)
        {
            return new TrendDeduplicationResult(Array.Empty<TrendEvidence>(), Array.Empty<TrendSuppressedGroup>());
        }

        var detector = new TrendEvidenceDuplicateDetector(0.45);
        var candidates = evidence.Select(item => new
            {
                Trend = item,
                Candidate = detector.CreateCandidate(item, ResolveSourceDefinitionId(item, rawSourcesById), item.TrendFamily)
            })
            .ToArray();

        var canonical = new List<TrendEvidence>();
        var suppressedGroups = new List<TrendSuppressedGroup>();

        foreach (var sourceGroup in candidates.GroupBy(entry => entry.Candidate.SourceDefinitionId))
        {
            var accepted = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
            var groupedByFamily = sourceGroup
                .GroupBy(entry => entry.Candidate.TrendFamily)
                .OrderBy(group => group.Key == TrendFamily.Unknown ? 1 : 0)
                .ThenBy(group => group.Key.ToString())
                .ToArray();

            foreach (var familyGroup in groupedByFamily)
            {
                var familyAccepted = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
                var familyAcceptedEvidence = new List<TrendEvidence>();
                var suppressed = new List<string>();

                var canonicalKeyGroups = familyGroup
                    .GroupBy(entry => BuildCanonicalGroupKey(entry.Candidate))
                    .ToArray();

                var canonicalGroupEntries = canonicalKeyGroups
                    .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                    .ToArray();

                foreach (var canonicalGroup in canonicalGroupEntries)
                {
                    var bestEntry = canonicalGroup
                        .OrderByDescending(entry => ScoreCandidateSpecificity(entry.Candidate))
                        .First();

                    familyAccepted.Add(bestEntry.Candidate);
                    familyAcceptedEvidence.Add(bestEntry.Trend);

                    foreach (var entry in canonicalGroup.Where(entry => !ReferenceEquals(entry, bestEntry)))
                    {
                        suppressed.Add(entry.Trend.Topic);
                    }
                }

                var familyCandidates = canonicalKeyGroups
                    .Where(group => string.IsNullOrWhiteSpace(group.Key))
                    .SelectMany(group => group)
                    .OrderByDescending(entry => ScoreCandidateSpecificity(entry.Candidate))
                    .ToList();

                foreach (var entry in familyCandidates)
                {
                    var candidate = entry.Candidate;
                    if (candidate.TrendFamily == TrendFamily.Unknown)
                    {
                        var specificMatch = accepted.FirstOrDefault(existing =>
                            existing.TrendFamily != TrendFamily.Unknown
                            && IsDuplicateIgnoringPeriod(detector, candidate, existing));
                        if (specificMatch is not null)
                        {
                            suppressed.Add(entry.Trend.Topic);
                            continue;
                        }
                    }

                    if (familyAccepted.Any(existing => IsDuplicateIgnoringPeriod(detector, candidate, existing)))
                    {
                        suppressed.Add(entry.Trend.Topic);
                        continue;
                    }

                    familyAccepted.Add(candidate);
                    familyAcceptedEvidence.Add(entry.Trend);
                }

                canonical.AddRange(familyAcceptedEvidence);
                accepted.AddRange(familyAccepted);

                if (suppressed.Count > 0)
                {
                    var canonicalCandidate = familyAccepted.FirstOrDefault();
                    var canonicalTrend = familyAcceptedEvidence.FirstOrDefault() ?? familyGroup.First().Trend;
                    suppressedGroups.Add(new TrendSuppressedGroup(
                        canonicalCandidate?.RawTopic ?? canonicalTrend.Topic,
                        canonicalCandidate?.BestPeriod ?? canonicalTrend.Period,
                        familyGroup.Key,
                        suppressed));
                }
            }
        }

        return new TrendDeduplicationResult(
            canonical.GroupBy(entry => entry.Id).Select(group => group.First()).ToArray(),
            suppressedGroups);

        static Guid ResolveSourceDefinitionId(TrendEvidence evidence, IReadOnlyDictionary<Guid, RawSourceItemSummary> rawSourcesById)
        {
            return rawSourcesById.TryGetValue(evidence.SourceItemId, out var rawSource)
                ? rawSource.SourceDefinitionId
                : Guid.Empty;
        }

        static int ScoreCandidateSpecificity(TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate candidate)
        {
            var findingScore = candidate.RawFinding?.Length ?? 0;
            var summaryScore = candidate.RawEvidenceSummary?.Length ?? 0;
            var periodScore = ScorePeriodSpecificity(candidate.BestPeriod, candidate.BestPeriodProvenance);
            return (findingScore * 3) + (summaryScore * 2) + periodScore;
        }

        static int ScorePeriodSpecificity(string period, TrendEvidencePeriodProvenance provenance)
        {
            if (!string.IsNullOrWhiteSpace(period)
                && !string.Equals(period, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return provenance switch
                {
                    TrendEvidencePeriodProvenance.SourceContent => 4,
                    TrendEvidencePeriodProvenance.SourceMetadata => 3,
                    TrendEvidencePeriodProvenance.PublicationDate => 2,
                    _ => 1
                };
            }

            return 0;
        }

        static string BuildCanonicalGroupKey(TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate.CanonicalTrendKey))
            {
                var parts = candidate.CanonicalTrendKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2
                    ? string.Join(':', parts[0], parts[1])
                    : candidate.CanonicalTrendKey;
            }

            var normalizedTopic = NormalizeForGrouping(candidate.NormalizedTopic);
            if (!string.IsNullOrWhiteSpace(normalizedTopic))
            {
                return string.Join('|', candidate.SourceDefinitionId, normalizedTopic, candidate.TrendFamily);
            }

            var normalizedFinding = NormalizeForGrouping(candidate.NormalizedFinding);
            return string.Join('|', candidate.SourceDefinitionId, normalizedFinding, candidate.TrendFamily);
        }

        static string NormalizeForGrouping(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized.Length > 120 ? normalized[..120] : normalized;
        }

        static bool IsDuplicateIgnoringPeriod(
            TrendEvidenceDuplicateDetector detector,
            TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate candidate,
            TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate existing)
        {
            var similarity = detector.EvaluateSimilarity(candidate, existing);
            if (similarity.IsDuplicate)
            {
                return true;
            }

            if (similarity.Reason.StartsWith("Period mismatch", StringComparison.Ordinal)
                || similarity.Reason.StartsWith("Period mismatch.", StringComparison.Ordinal))
            {
                var combinedScore = (0.20 * similarity.TopicSimilarity)
                    + (0.40 * similarity.FindingSimilarity)
                    + (0.25 * similarity.EvidenceSimilarity)
                    + (0.15 * similarity.ConceptSimilarity);
                return combinedScore >= detector.DuplicateThreshold;
            }

            return false;
        }
    }

    private sealed record TrendDeduplicationResult(
        IReadOnlyCollection<TrendEvidence> CanonicalTrends,
        IReadOnlyCollection<TrendSuppressedGroup> SuppressedGroups);

    private sealed record TrendSuppressedGroup(
        string CanonicalTopic,
        string CanonicalPeriod,
        TrendFamily TrendFamily,
        IReadOnlyCollection<string> SuppressedTopics);

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
