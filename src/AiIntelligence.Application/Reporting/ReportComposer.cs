using System.Text;
using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Reporting;

internal static class ReportComposer
{
    public static ComposedReport Compose(ReportDocument doc,
        int maxDevelopments = 5,
        int maxTrends = 12,
        int maxImplications = 5,
        int maxPersonaActions = 5,
        int maxPersonaQuestions = 3,
        int maxPersonaWatch = 3)
    {
        // Executive highlights: top developments and trends from structured data
        var topDevelopments = doc.CurrentDevelopments.Take(maxDevelopments).ToArray();

        // Canonicalize broader trends by normalized topic + family
        var canonicalTrends = CanonicalizeTrends(doc.BroaderTrends).Take(maxTrends).ToArray();

        // Cross-cutting implications: simplistic extraction from trends/confidence
        var implications = canonicalTrends
            .OrderByDescending(t => t.Confidence)
            .Take(maxImplications)
            .Select(t => t.Trend)
            .ToArray();

        // Persona compression: trim arrays and keep essential fields
        var personas = doc.PersonaSections.Select(p =>
            new PersonaReportSection(
                p.PersonaType,
                p.Relevance,
                p.Heading,
                p.RelevantDevelopment,
                p.SpecificImpact,
                p.TrendImplication,
                p.RecommendedActions.Take(maxPersonaActions).ToArray(),
                p.QuestionsToExplore.Take(maxPersonaQuestions).ToArray(),
                p.WatchItems.Take(maxPersonaWatch).ToArray(),
                p.RelevanceDetail
            )).ToArray();

        // Source references: dedupe deterministically by URL and preserve classification
        var sourceList = doc.SourceReferences
            .GroupBy(s => s.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Label)
            .ThenBy(s => s.Url.AbsoluteUri)
            .ToArray();

        // Build URL->index map (1-based) for compact references in main report
        var sourceIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sourceList.Length; i++)
        {
            sourceIndex[sourceList[i].Url.AbsoluteUri] = i + 1;
        }

        // Compose compact correlation items with ShortRationale (first 2 sentences)
        var composedCorrelations = doc.Correlations.Select(c =>
            new ComposedCorrelation(
                c.IntelligenceItemId,
                c.CurrentDevelopment,
                c.RelatedTrend,
                c.Relationship,
                ShortRationale(c.Explanation),
                c.Explanation,
                c.Confidence,
                c.SupportingSourceUrls.Select(u => sourceIndex.TryGetValue(u.AbsoluteUri, out var idx) ? idx : 0).Where(i => i > 0).ToArray(),
                c.SupportingSourceUrls)).ToArray();

        return new ComposedReport(doc.GeneratedAt, doc.ReportingPeriod, doc.OverallSummary,
            topDevelopments, canonicalTrends, composedCorrelations, personas, sourceList, implications);
    }

    private static string ShortRationale(string explanation)
    {
        if (string.IsNullOrWhiteSpace(explanation)) return string.Empty;
        // heuristically take first two sentences (split on period + space)
        var sentences = explanation.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length == 0) return explanation.Length <= 280 ? explanation : explanation.Substring(0, 280) + "...";
        var take = Math.Min(2, sentences.Length);
        var joined = string.Join(". ", sentences.Take(take));
        if (!joined.EndsWith('.') && explanation.Contains('.')) joined += ".";
        return joined.Length <= 400 ? joined : joined.Substring(0, 400).TrimEnd() + "...";
    }

    private static IEnumerable<ReportTrend> CanonicalizeTrends(IReadOnlyCollection<ReportTrend> trends)
    {
        // Group by family and normalized topic tokens to collapse near duplicates
        var groups = new Dictionary<string, List<ReportTrend>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trends)
        {
            var key = (t.Trend ?? string.Empty).ToLowerInvariant();
            key = string.Join(' ', key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6));
            key = string.Concat(t.PeriodProvenance.ToString(), "|", key, "|", t.Confidence.ToString("0.##"));
            if (!groups.TryGetValue(key, out var list)) { list = new List<ReportTrend>(); groups[key] = list; }
            list.Add(t);
        }

        // pick representative: highest confidence, then longest evidence
        var reps = groups.Values.Select(list => list.OrderByDescending(t => t.Confidence)
            .ThenByDescending(t => (t.SupportingEvidence?.Length ?? 0)).First()).ToArray();

        // order by confidence and distinctness
        return reps.OrderByDescending(t => t.Confidence).ThenByDescending(t => (t.SupportingEvidence?.Length ?? 0));
    }
}

internal sealed record ComposedReport(
    DateTimeOffset GeneratedAt,
    string ReportingPeriod,
    string OverallSummary,
    IReadOnlyCollection<ReportCurrentDevelopment> KeyDevelopments,
    IReadOnlyCollection<ReportTrend> CanonicalTrends,
    IReadOnlyCollection<ComposedCorrelation> Correlations,
    IReadOnlyCollection<PersonaReportSection> PersonaSections,
    IReadOnlyCollection<SourceReference> SourceReferences,
    IReadOnlyCollection<string> CrossCuttingImplications);

internal sealed record ComposedCorrelation(
    Guid IntelligenceItemId,
    string CurrentDevelopment,
    string RelatedTrend,
    CorrelationRelationship Relationship,
    string ShortRationale,
    string FullExplanation,
    decimal Confidence,
    IReadOnlyCollection<int> SourceIndices,
    IReadOnlyCollection<Uri> Sources);
