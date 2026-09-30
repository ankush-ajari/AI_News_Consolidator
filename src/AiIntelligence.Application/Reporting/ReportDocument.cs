using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Reporting;

public sealed record ReportDocument(
    DateTimeOffset GeneratedAt,
    string ReportingPeriod,
    string OverallSummary,
    IReadOnlyCollection<ReportCurrentDevelopment> CurrentDevelopments,
    IReadOnlyCollection<ReportTrend> BroaderTrends,
    IReadOnlyCollection<TrendCorrelation> Correlations,
    IReadOnlyCollection<PersonaReportSection> PersonaSections,
    IReadOnlyCollection<SourceReference> SourceReferences);

public sealed record ReportCurrentDevelopment(
    string Headline,
    string WhatChanged,
    string VendorOrTechnology,
    string WhyItMatters,
    Uri SourceUrl);

public sealed record ReportTrend(
    string Trend,
    string SupportingEvidence,
    string TimePeriod,
    TrendEvidencePeriodProvenance PeriodProvenance,
    decimal Confidence,
    IReadOnlyCollection<Uri> SourceUrls);

public sealed record PersonaReportSection(
    PersonaType PersonaType,
    PersonaRelevance Relevance,
    string Heading,
    string RelevantDevelopment,
    string SpecificImpact,
    string TrendImplication,
    IReadOnlyCollection<string> RecommendedActions,
    IReadOnlyCollection<string> QuestionsToExplore,
    IReadOnlyCollection<string> WatchItems,
    string RelevanceDetail);

public sealed record SourceReference(string Label, Uri Url, string SourceClass);
