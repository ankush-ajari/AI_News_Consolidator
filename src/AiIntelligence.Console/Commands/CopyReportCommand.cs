using AiIntelligence.Application.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace AiIntelligence.Console.Commands;

public static class CopyReportCommand
{
    /// <summary>
    /// Execute the standalone copy-report command using the provided service provider.
    /// Returns the publish result from the configured IReportFilePublisher.
    /// </summary>
    public static async Task<ReportPublishResult> ExecuteAsync(
        IServiceProvider services,
        string? inputPath,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<AiIntelligence.Application.Reporting.IReportFilePublisher>();

        var defaultDocx = Path.Combine(Environment.CurrentDirectory, "output", "ai-intelligence-report.docx");
        var sourcePath = string.IsNullOrWhiteSpace(inputPath) ? defaultDocx : inputPath!;

        var publishResult = await publisher.PublishDocxAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return publishResult;
    }
}
