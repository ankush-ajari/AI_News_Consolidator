using AiIntelligence.Application.Reporting;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public sealed class GeneratePersonaReportTool
{
    private readonly IPersonaReportGenerator _reportGenerator;

    public GeneratePersonaReportTool(IPersonaReportGenerator reportGenerator)
    {
        _reportGenerator = reportGenerator;
    }

    public Task<ReportDocument> ExecuteAsync(
        GeneratePersonaReportInput input,
        CancellationToken cancellationToken)
    {
        return _reportGenerator.GenerateAsync(
            input.IntelligenceItems,
            input.Correlations,
            input.TrendEvidence,
            cancellationToken);
    }
}

public sealed record GeneratePersonaReportInput(
    IReadOnlyCollection<IntelligenceItem> IntelligenceItems,
    IReadOnlyCollection<TrendCorrelation> Correlations,
    IReadOnlyCollection<TrendEvidence> TrendEvidence);
