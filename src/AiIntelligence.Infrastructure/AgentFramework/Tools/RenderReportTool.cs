using AiIntelligence.Application.Reporting;

namespace AiIntelligence.Infrastructure.AgentFramework.Tools;

public sealed class RenderReportTool
{
    private readonly MarkdownReportRenderer _renderer;

    public RenderReportTool(MarkdownReportRenderer renderer)
    {
        _renderer = renderer;
    }

    public string Execute(RenderReportInput input)
    {
        return _renderer.Render(input.Document);
    }
}

public sealed record RenderReportInput(ReportDocument Document);
