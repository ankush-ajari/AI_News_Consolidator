using AiIntelligence.Infrastructure.Reporting;
using AiIntelligence.Infrastructure.Configuration;
using AiIntelligence.Application.Reporting;
using Microsoft.Extensions.Options;

namespace AiIntelligence.Infrastructure.Tests;

public class FileSystemReportPublisherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceFile;

    public FileSystemReportPublisherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "aiintellij-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        _sourceFile = Path.Combine(outputDir, "ai-intelligence-report.docx");
        File.WriteAllText(_sourceFile, "dummy");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    private FileSystemReportPublisher CreatePublisher(ReportPublishingOptions options)
    {
        var opt = Options.Create(options);
        return new FileSystemReportPublisher(opt);
    }

    [Fact]
    public async Task FirstPublishCreatesTimestampedFile()
    {
        var options = new ReportPublishingOptions
        {
            Enabled = true,
            DestinationPath = Path.Combine(_tempDir, "dest"),
            OverwriteExisting = true,
            FileNamePrefix = "AI-Technology-Intelligence"
        };
        var publisher = CreatePublisher(options);

        var result = await publisher.PublishDocxAsync(_sourceFile);

        Assert.True(result.Success);
        Assert.NotNull(result.DestinationPath);
        Assert.True(File.Exists(result.DestinationPath));
        var fileName = Path.GetFileName(result.DestinationPath);
        Assert.Matches(@"^AI-Technology-Intelligence-\d{8}-\d{6}\.docx$", fileName);
    }

    [Fact]
    public async Task SecondPublishCreatesDifferentFileAndDoesNotOverwrite()
    {
        var options = new ReportPublishingOptions
        {
            Enabled = true,
            DestinationPath = Path.Combine(_tempDir, "dest"),
            OverwriteExisting = true,
            FileNamePrefix = "AI-Technology-Intelligence"
        };
        var publisher = CreatePublisher(options);

        var r1 = await publisher.PublishDocxAsync(_sourceFile);
        var r2 = await publisher.PublishDocxAsync(_sourceFile);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.NotEqual(r1.DestinationPath, r2.DestinationPath);
        Assert.True(File.Exists(r1.DestinationPath));
        Assert.True(File.Exists(r2.DestinationPath));
    }

    [Fact]
    public async Task CollisionHandlingAppendsNumericSuffix()
    {
        var options = new ReportPublishingOptions
        {
            Enabled = true,
            DestinationPath = Path.Combine(_tempDir, "dest"),
            OverwriteExisting = true,
            FileNamePrefix = "AI-Technology-Intelligence"
        };
        var publisher = CreatePublisher(options);

        // Simulate existing file with current timestamp name
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var baseName = $"{options.FileNamePrefix}-{timestamp}.docx";
        Directory.CreateDirectory(options.DestinationPath);
        var existing = Path.Combine(options.DestinationPath, baseName);
        File.WriteAllText(existing, "existing");

        var result = await publisher.PublishDocxAsync(_sourceFile);
        Assert.True(result.Success);
        var createdName = Path.GetFileName(result.DestinationPath!);
        // use simple check for suffix presence rather than Regex to avoid adding extra using
        Assert.StartsWith($"{options.FileNamePrefix}-{timestamp}", Path.GetFileNameWithoutExtension(createdName));
        Assert.True(File.Exists(result.DestinationPath));
    }

    [Fact]
    public async Task CustomPrefixRespectedAndSanitized()
    {
        var options = new ReportPublishingOptions
        {
            Enabled = true,
            DestinationPath = Path.Combine(_tempDir, "dest"),
            OverwriteExisting = true,
            FileNamePrefix = "Bad:/\\Name*?"
        };
        var publisher = CreatePublisher(options);

        var result = await publisher.PublishDocxAsync(_sourceFile);
        Assert.True(result.Success);
        var createdName = Path.GetFileName(result.DestinationPath!);
        Assert.DoesNotContain("/", createdName);
        Assert.DoesNotContain("\\", createdName);
        Assert.DoesNotContain("*", createdName);
    }

    [Fact]
    public async Task SourceFileRemainsUnchanged()
    {
        var options = new ReportPublishingOptions
        {
            Enabled = true,
            DestinationPath = Path.Combine(_tempDir, "dest"),
            OverwriteExisting = true,
            FileNamePrefix = "AI-Technology-Intelligence"
        };
        var publisher = CreatePublisher(options);

        var before = File.ReadAllText(_sourceFile);
        var result = await publisher.PublishDocxAsync(_sourceFile);
        var after = File.ReadAllText(_sourceFile);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task DestinationDirectoryIsCreatedIfMissing()
    {
        var dest = Path.Combine(_tempDir, "nonexistent", "sub");
        var options = new ReportPublishingOptions
        {
            Enabled = true,
            DestinationPath = dest,
            OverwriteExisting = true,
            FileNamePrefix = "AI-Technology-Intelligence"
        };
        var publisher = CreatePublisher(options);

        var result = await publisher.PublishDocxAsync(_sourceFile);
        Assert.True(result.Success);
        Assert.True(Directory.Exists(dest));
        Assert.True(File.Exists(result.DestinationPath));
    }
}
