using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public interface IAnalyzeCurrentIntelligenceTool
{
    Task<IntelligenceAnalysisResult> ExecuteAsync(AnalyzeCurrentIntelligenceInput input, CancellationToken cancellationToken);
}

public interface IAnalyzeTrendEvidenceTool
{
    Task<TrendAnalysisResult> ExecuteAsync(AnalyzeTrendEvidenceInput input, CancellationToken cancellationToken);
}

public interface ICorrelateCurrentDevelopmentTool
{
    Task<CorrelateCurrentDevelopmentResult> ExecuteAsync(CorrelateCurrentDevelopmentInput input, CancellationToken cancellationToken);
}

public interface IGeneratePersonaReportTool
{
    Task<ReportDocument> ExecuteAsync(GeneratePersonaReportInput input, CancellationToken cancellationToken);
}

public interface IRenderReportTool
{
    string Execute(RenderReportInput input);
}

public interface IIngestSourcesTool
{
    Task<SourceIngestResult> ExecuteAsync(IngestSourcesInput input, CancellationToken cancellationToken);
}
