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

            // Build base destination filename using sanitized prefix and timestamp
            var prefix = string.IsNullOrWhiteSpace(_options.FileNamePrefix) ? "AI-Technology-Intelligence" : _options.FileNamePrefix;
            // Sanitize prefix: remove path separators and invalid filename chars
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }).Distinct().ToArray();
            var safePrefix = new string(prefix.Where(ch => !invalid.Contains(ch)).ToArray());
            if (string.IsNullOrWhiteSpace(safePrefix))
            {
                safePrefix = "AI-Technology-Intelligence";
            }

            // Use local time (system) formatted to seconds to be consistent with app conventions
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var baseName = $"{safePrefix}-{timestamp}.docx";
            var destPath = Path.Combine(destinationDir, baseName);

            // If destination already exists, append a numeric suffix to avoid overwrite
            var attempt = 1;
            var destFull = Path.GetFullPath(destPath);
            while (File.Exists(destFull))
            {
                var numbered = $"{Path.GetFileNameWithoutExtension(baseName)}-{attempt}{Path.GetExtension(baseName)}";
                destFull = Path.Combine(destinationDir, numbered);
                attempt++;
            }

            // Perform copy (do not load into memory)
            File.Copy(sourceDocxPath, destFull, overwrite: false);
            return Task.FromResult(new ReportPublishResult(true, sourceDocxPath, destFull, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ReportPublishResult(false, sourceDocxPath, null, ex.Message));
        }
    }
}
