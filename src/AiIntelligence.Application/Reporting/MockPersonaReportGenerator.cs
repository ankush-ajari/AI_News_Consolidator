using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Reporting;

public sealed class MockPersonaReportGenerator : IPersonaReportGenerator
{
    public Task<ReportDocument> GenerateAsync(IReadOnlyCollection<IntelligenceItem> intelligenceItems, IReadOnlyCollection<TrendCorrelation> correlations, IReadOnlyCollection<TrendEvidence> trendEvidence, CancellationToken cancellationToken)
    {
        var personas = new[] { PersonaType.Developer, PersonaType.QA, PersonaType.BusinessAnalyst, PersonaType.ProjectManager, PersonaType.Sales }
            .Select(persona => new PersonaReportSection(
                persona,
                PersonaRelevance.Medium,
                persona.ToString(),
                $"Mock {persona}: review {intelligenceItems.FirstOrDefault()?.Topic ?? "current AI developments"}.",
                persona switch
                {
                    PersonaType.Developer => "Implementation impact across APIs, SDKs, architecture, and integration patterns.",
                    PersonaType.QA => "Testing impact across evaluation, nondeterminism, regression, observability, and reliability.",
                    PersonaType.BusinessAnalyst => "Requirement and workflow impact for use cases, acceptance criteria, and stakeholder questions.",
                    PersonaType.ProjectManager => "Delivery impact across dependencies, schedule, risks, governance, and pilot readiness.",
                    PersonaType.Sales => "Customer conversation impact across differentiation, adoption signals, caveats, and what not to oversell.",
                    _ => "Role-specific impact should be reviewed."
                },
                correlations.FirstOrDefault()?.RelatedTrend ?? "Unknown",
                persona switch
                {
                    PersonaType.Developer => new[] { "Prototype API usage", "Assess SDK fit", "Review migration implications" },
                    PersonaType.QA => new[] { "Design evaluation cases", "Plan nondeterministic regression tests", "Review observability tooling" },
                    PersonaType.BusinessAnalyst => new[] { "Refine acceptance criteria", "Map workflow impact", "Prepare stakeholder questions" },
                    PersonaType.ProjectManager => new[] { "Assess delivery risk", "Plan pilot milestones", "Identify dependency owners" },
                    PersonaType.Sales => new[] { "Frame customer discussion themes", "Document limitations", "Avoid overselling unproven claims" },
                    _ => new[] { "Review implications" }
                },
                persona switch
                {
                    PersonaType.BusinessAnalyst => new[] { "Which requirements change?", "Which workflows are affected?" },
                    PersonaType.ProjectManager => new[] { "What risks affect delivery?", "What governance is needed?" },
                    _ => new[] { "What evidence is actionable?", "What caveats remain?" }
                },
                new[] { correlations.FirstOrDefault()?.Relationship.ToString() ?? "InsufficientEvidence" },
                "Mock relevance based on supplied evidence."))
            .ToArray();

        var doc = new ReportDocument(
            DateTimeOffset.UtcNow,
            "Mock",
            "Mock report generated for workflow validation only.",
            intelligenceItems.Select(item => new ReportCurrentDevelopment(item.Topic, item.Summary, item.ProductOrFramework, "Mock relevance summary.", item.SourceUrl)).ToArray(),
            trendEvidence.Select(trend => new ReportTrend(trend.Topic, trend.EvidenceSummary, trend.Period, trend.PeriodProvenance, trend.Confidence, new[] { trend.SourceUrl })).ToArray(),
            correlations,
            personas,
            intelligenceItems.Select(item => new SourceReference($"CurrentOfficial: {item.Topic}", item.SourceUrl, "CurrentOfficial"))
                .Concat(trendEvidence.Select(trend => new SourceReference($"TrendResearch: {trend.Topic}", trend.SourceUrl, "TrendResearch")))
                .ToArray());
        return Task.FromResult(doc);
    }
}
