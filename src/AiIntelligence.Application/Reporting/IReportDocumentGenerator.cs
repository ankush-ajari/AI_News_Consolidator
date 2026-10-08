namespace AiIntelligence.Application.Reporting;

public interface IReportDocumentGenerator
{
    Task<string> GenerateDocxAsync(
        string markdownPath,
        string outputPath,
        CancellationToken cancellationToken = default);
}
