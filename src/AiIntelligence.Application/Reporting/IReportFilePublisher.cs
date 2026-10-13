namespace AiIntelligence.Application.Reporting;

public interface IReportFilePublisher
{
    Task<ReportPublishResult> PublishDocxAsync(string sourceDocxPath, CancellationToken cancellationToken = default);
}

public sealed record ReportPublishResult(bool Success, string SourcePath, string? DestinationPath, string? ErrorMessage, bool Skipped = false);
