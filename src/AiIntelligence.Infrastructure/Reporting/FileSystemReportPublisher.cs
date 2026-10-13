using AiIntelligence.Application.Reporting;
using AiIntelligence.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AiIntelligence.Infrastructure.Reporting;

public sealed class FileSystemReportPublisher : IReportFilePublisher
{
    private readonly ReportPublishingOptions _options;

    public FileSystemReportPublisher(IOptions<ReportPublishingOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ReportPublishResult> PublishDocxAsync(string sourceDocxPath, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(new ReportPublishResult(false, sourceDocxPath, null, "Publishing disabled by configuration.", Skipped: true));
        }

        if (string.IsNullOrWhiteSpace(_options.DestinationPath))
        {
            return Task.FromResult(new ReportPublishResult(false, sourceDocxPath, null, "DestinationPath is not configured."));
        }

        if (!File.Exists(sourceDocxPath))
        {
            return Task.FromResult(new ReportPublishResult(false, sourceDocxPath, null, "Source DOCX not found."));
        }

        var destinationDir = _options.DestinationPath!;
        try
        {
            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            var fileName = Path.GetFileName(sourceDocxPath)!;
            var destPath = Path.Combine(destinationDir, fileName);

            if (string.Equals(Path.GetFullPath(sourceDocxPath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ReportPublishResult(true, sourceDocxPath, destPath, null));
            }

            if (File.Exists(destPath) && !_options.OverwriteExisting)
            {
                return Task.FromResult(new ReportPublishResult(false, sourceDocxPath, destPath, "Destination file exists and overwrite is disabled."));
            }

            File.Copy(sourceDocxPath, destPath, _options.OverwriteExisting);
            return Task.FromResult(new ReportPublishResult(true, sourceDocxPath, destPath, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ReportPublishResult(false, sourceDocxPath, null, ex.Message));
        }
    }
}
