using System.Text;
using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Reporting;

public sealed class MarkdownReportRenderer
{
    public string Render(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI Technology Intelligence Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {document.GeneratedAt:O}");
        builder.AppendLine($"Reporting period: {document.ReportingPeriod}");
        builder.AppendLine();
        builder.AppendLine("## Executive / Overall Summary");
        builder.AppendLine(document.OverallSummary);
        builder.AppendLine();
        builder.AppendLine("## Key Current Developments");
        foreach (var item in document.CurrentDevelopments)
        {
            builder.AppendLine($"### {item.Headline}");
            builder.AppendLine($"- What changed: {item.WhatChanged}");
            builder.AppendLine($"- Vendor / technology: {item.VendorOrTechnology}");
            builder.AppendLine($"- Why it matters: {item.WhyItMatters}");
            builder.AppendLine($"- Source: {item.SourceUrl}");
            builder.AppendLine();
        }

        builder.AppendLine("## Broader AI Trends");
        foreach (var trend in document.BroaderTrends)
        {
            builder.AppendLine($"### {trend.Trend}");
            builder.AppendLine($"- Supporting evidence: {trend.SupportingEvidence}");
            builder.AppendLine($"- Time period: {trend.TimePeriod} ({trend.PeriodProvenance})");
            builder.AppendLine($"- Confidence: {trend.Confidence}");
            builder.AppendLine($"- Sources: {string.Join(", ", trend.SourceUrls)}");
            builder.AppendLine();
        }

        builder.AppendLine("## Current Development vs Trend");
        foreach (var correlation in document.Correlations)
        {
            builder.AppendLine($"### {correlation.Relationship}: {correlation.CurrentDevelopment}");
            builder.AppendLine($"- Related trend: {correlation.RelatedTrend}");
            builder.AppendLine($"- Explanation: {correlation.Explanation}");
            builder.AppendLine($"- Confidence: {correlation.Confidence}");
            builder.AppendLine($"- Sources: {string.Join(", ", correlation.SupportingSourceUrls)}");
            builder.AppendLine();
        }

        builder.AppendLine("## Persona Sections");
        foreach (var persona in document.PersonaSections)
        {
            builder.AppendLine($"### {persona.Heading}");
            builder.AppendLine($"- Relevance: {persona.Relevance} ({persona.RelevanceDetail})");
            builder.AppendLine($"- Relevant development: {persona.RelevantDevelopment}");
            builder.AppendLine($"- Specific impact: {persona.SpecificImpact}");
            builder.AppendLine($"- Trend implication: {persona.TrendImplication}");
            AppendOptionalList(builder, "Recommended actions", persona.RecommendedActions, persona.Relevance);
            AppendOptionalList(builder, "Questions to explore", persona.QuestionsToExplore, persona.Relevance);
            AppendOptionalList(builder, "Watch items", persona.WatchItems, persona.Relevance);
            builder.AppendLine();
        }

        builder.AppendLine("## Source References");
        foreach (var reference in document.SourceReferences)
        {
            builder.AppendLine($"- [{reference.SourceClass}] {reference.Label}: {reference.Url}");
        }

        return builder.ToString();
    }

    private static void AppendOptionalList(StringBuilder builder, string label, IReadOnlyCollection<string> items, PersonaRelevance relevance)
    {
        if (items.Count == 0)
        {
            if (relevance is PersonaRelevance.Low or PersonaRelevance.NotRelevant)
            {
                return;
            }

            builder.AppendLine($"- {label}: None at this time.");
            return;
        }

        builder.AppendLine($"- {label}: {string.Join("; ", items)}");
    }
}
