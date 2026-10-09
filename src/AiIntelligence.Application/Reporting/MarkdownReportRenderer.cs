using System.Text;
using AiIntelligence.Domain.Enums;
using System.Linq;

namespace AiIntelligence.Application.Reporting;

public sealed class MarkdownReportRenderer
{
    public string Render(ReportDocument document)
    {
        var composer = ReportComposer.Compose(document);
        var builder = new StringBuilder();
        builder.AppendLine("# AI Technology Intelligence Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {composer.GeneratedAt:O}");
        builder.AppendLine($"Reporting period: {composer.ReportingPeriod}");
        builder.AppendLine();
        builder.AppendLine("## Executive Highlights");
        builder.AppendLine();
        builder.AppendLine("### Top developments");
        foreach (var item in composer.KeyDevelopments)
        {
            builder.AppendLine($"- {item.Headline} — {item.WhatChanged} ({item.VendorOrTechnology})");
        }
        builder.AppendLine();
        builder.AppendLine("### Broader signals");
        foreach (var trend in composer.CanonicalTrends.Take(5))
        {
            builder.AppendLine($"- {trend.Trend} — {Shorten(trend.SupportingEvidence, 160)}");
        }
        builder.AppendLine();
        builder.AppendLine("### Cross-cutting implications");
        foreach (var impl in composer.CrossCuttingImplications.Take(5))
        {
            builder.AppendLine($"- {Shorten(impl, 200)}");
        }
        builder.AppendLine();

        builder.AppendLine("## Key Current Developments");
        foreach (var item in composer.KeyDevelopments)
        {
            builder.AppendLine($"### {item.Headline}");
            builder.AppendLine($"**What changed**: {Shorten(item.WhatChanged, 300)}");
            builder.AppendLine($"**Technology**: {Shorten(item.VendorOrTechnology, 120)}");
            builder.AppendLine($"**Why it matters**: {Shorten(item.WhyItMatters, 200)}");
            builder.AppendLine($"**Release stage**: (unknown)");
            builder.AppendLine($"**Source**: {item.SourceUrl}");
            builder.AppendLine();
        }

        builder.AppendLine("## Broader AI Trends");
        foreach (var trend in composer.CanonicalTrends)
        {
            builder.AppendLine($"### {trend.Trend}");
            builder.AppendLine($"**Supporting evidence**: {Shorten(trend.SupportingEvidence, 300)}");
            builder.AppendLine($"**Time period**: {trend.TimePeriod} ({trend.PeriodProvenance})");
            builder.AppendLine($"**Confidence**: {trend.Confidence:0.##}");
            builder.AppendLine($"**Sources**: {string.Join(", ", trend.SourceUrls)}");
            builder.AppendLine();
        }

        builder.AppendLine("## Development ↔ Trend Map");
        foreach (var correlation in composer.Correlations)
        {
            // Find a matching canonical trend to extract a short trend signal
            var trend = composer.CanonicalTrends.FirstOrDefault(t => string.Equals(t.Trend, correlation.RelatedTrend, StringComparison.OrdinalIgnoreCase));
            var trendSignal = trend is null ? string.Empty : Shorten((trend.SupportingEvidence ?? string.Empty).Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty, 200);

            builder.AppendLine($"### {correlation.CurrentDevelopment} → {correlation.RelatedTrend}");
            builder.AppendLine($"**Relationship:** {correlation.Relationship}");
            builder.AppendLine($"**Confidence:** {correlation.Confidence:0.##}");
            builder.AppendLine();
            builder.AppendLine("- **Current signal:** " + Shorten(correlation.ShortRationale ?? correlation.CurrentDevelopment, 200));
            builder.AppendLine("- **Trend signal:** " + (string.IsNullOrEmpty(trendSignal) ? "(see appendix)" : trendSignal));
            builder.AppendLine("- **Why this matters:** " + Shorten(correlation.ShortRationale, 240));
            // compact source index references
            if (correlation.SourceIndices is not null && correlation.SourceIndices.Count > 0)
            {
                builder.AppendLine("**Sources:** " + string.Join(", ", correlation.SourceIndices.Select(i => $"[{i}]") ));
            }
            builder.AppendLine();
        }

        builder.AppendLine("## Persona Sections");
        foreach (var persona in document.PersonaSections)
        {
            builder.AppendLine($"### {persona.Heading}");
            builder.AppendLine($"**Relevance**: {persona.Relevance} ({persona.RelevanceDetail})");
            builder.AppendLine($"**Why it matters**: {Shorten(persona.SpecificImpact, 300)}");
            AppendOptionalList(builder, "Recommended actions", persona.RecommendedActions, persona.Relevance, Math.Min(5, persona.RecommendedActions.Count));
            AppendOptionalList(builder, "Questions to explore", persona.QuestionsToExplore, persona.Relevance, Math.Min(3, persona.QuestionsToExplore.Count));
            AppendOptionalList(builder, "Watch items", persona.WatchItems, persona.Relevance, Math.Min(3, persona.WatchItems.Count));
            builder.AppendLine();
        }

        builder.AppendLine("## Source References");
        // Use the canonical composer source list and numbers when rendering Markdown
        // composer.SourceReferences is an IReadOnlyCollection; use ElementAt to index
        for (var i = 0; i < composer.SourceReferences.Count; i++)
        {
            var idx = i + 1;
            var reference = composer.SourceReferences.ElementAt(i);
            builder.AppendLine($"[{idx}] [{reference.SourceClass}] {reference.Label}");
            builder.AppendLine($"    {reference.Url}");
            builder.AppendLine();
        }

        // Evidence appendix
        builder.AppendLine();
        builder.AppendLine("## Evidence Appendix");
        builder.AppendLine();
        builder.AppendLine("### Detailed Correlation Evidence");
        foreach (var correlation in composer.Correlations)
        {
            builder.AppendLine($"#### {correlation.CurrentDevelopment} ↔ {correlation.RelatedTrend}");
            builder.AppendLine($"**Relationship:** {correlation.Relationship}");
            builder.AppendLine($"**Confidence:** {correlation.Confidence:0.##}");
            builder.AppendLine();
            builder.AppendLine("**Current evidence:**");
            builder.AppendLine("- " + Shorten(correlation.FullExplanation ?? string.Empty, 800));
            builder.AppendLine();
            builder.AppendLine("**Interpretation (full):**");
            builder.AppendLine(correlation.FullExplanation);
            if (correlation.SourceIndices is not null && correlation.SourceIndices.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("**Sources:**");
                foreach (var idx in correlation.SourceIndices)
                {
                    var refItem = composer.SourceReferences.ElementAtOrDefault(idx - 1);
                    if (refItem is not null)
                    {
                        builder.AppendLine($"[{idx}] [{refItem.SourceClass}] {refItem.Label}");
                        builder.AppendLine($"    {refItem.Url}");
                    }
                }
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendOptionalList(StringBuilder builder, string label, IReadOnlyCollection<string> items, PersonaRelevance relevance, int max = int.MaxValue)
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
        // Render as a sub-bulleted list for readability rather than an inline
        // semicolon-separated block. Respect a maximum number of items.
        builder.AppendLine($"- {label}:");
        var count = 0;
        foreach (var item in items)
        {
            if (count++ >= max) break;
            builder.AppendLine($"  - {item}");
        }
        if (items.Count > max)
        {
            builder.AppendLine($"  - ...and {items.Count - max} more");
        }
    }

    private static void AppendBulletedList(StringBuilder builder, IReadOnlyCollection<string> items, int max)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (count++ >= max) break;
            builder.AppendLine($"- {item}");
        }
        if (items.Count > max)
        {
            builder.AppendLine($"- ...and {items.Count - max} more");
        }
    }

    private static string Shorten(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= max) return text;
        return text.Substring(0, max).TrimEnd() + "...";
    }
}
