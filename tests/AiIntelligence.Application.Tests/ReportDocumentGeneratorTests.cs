using AiIntelligence.Application.Reporting;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace AiIntelligence.Application.Tests;

public sealed class ReportDocumentGeneratorTests
{
    [Fact]
    public void XmlSafeTextSanitizer_PreservesValidXml10Characters()
    {
        const string text = "ASCII, punctuation! Ελληνικά 日本語 😀\tcarriage\rreturn\nlinefeed";

        var result = XmlSafeTextSanitizer.Sanitize(text);

        Assert.Equal(text, result.Text);
        Assert.Equal(0, result.RemovedCharacterCount);
        Assert.Empty(result.RemovedCodePoints);
    }

    [Fact]
    public void XmlSafeTextSanitizer_RemovesOnlyInvalidXml10Controls()
    {
        const string text = "before\0\u0002\u000B\u000C\u001Fafter";

        var result = XmlSafeTextSanitizer.Sanitize(text);

        Assert.Equal("beforeafter", result.Text);
        Assert.Equal(5, result.RemovedCharacterCount);
        Assert.Equal(new[] { 0x00, 0x02, 0x0B, 0x0C, 0x1F }, result.RemovedCodePoints);
    }

    [Fact]
    public async Task GenerateDocxAsync_RemovesInvalidControlsAcrossMarkdownTextContexts()
    {
        var markdown = "# Heading\u0002 text\n\n- Bullet\u0002 text\n\nParagraph **bold\u0002 text** and [link\u0002 label](https://example.com/path) plus https://contoso.com/docs";
        var markdownPath = Path.GetTempFileName();
        var outputPath = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.docx");
        await File.WriteAllTextAsync(markdownPath, markdown, CancellationToken.None);

        try
        {
            var generator = new OpenXmlReportDocumentGenerator();
            var resultPath = await generator.GenerateDocxAsync(markdownPath, outputPath, CancellationToken.None);

            using var document = WordprocessingDocument.Open(resultPath, false);
            var textContent = string.Concat(document.MainDocumentPart!.Document.Body!
                .Descendants<Text>()
                .Select(text => text.Text));

            Assert.DoesNotContain('\u0002', textContent);
            Assert.Contains("Heading text", textContent, StringComparison.Ordinal);
            Assert.Contains("Bullet text", textContent, StringComparison.Ordinal);
            Assert.Contains("bold text", textContent, StringComparison.Ordinal);
            Assert.Contains("link label", textContent, StringComparison.Ordinal);
            Assert.Contains("https://contoso.com/docs", textContent, StringComparison.Ordinal);
            Assert.Equal(2, document.MainDocumentPart.Document.Body.Descendants<Hyperlink>().Count());
        }
        finally
        {
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task GenerateDocxAsync_ProducesDocxWithHeadingsAndBullets()
    {
        var markdown = new StringBuilder()
            .AppendLine("# AI Technology Intelligence Report")
            .AppendLine()
            .AppendLine("## Executive Summary")
            .AppendLine("Summary with a [link](https://example.com) and **bold text**.")
            .AppendLine()
            .AppendLine("### Highlights")
            .AppendLine("- Bullet one")
            .AppendLine("- Bullet two")
            .AppendLine()
            .AppendLine("Plain paragraph with https://contoso.com/docs")
            .ToString();

        var markdownPath = Path.GetTempFileName();
        var outputPath = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.docx");
        await File.WriteAllTextAsync(markdownPath, markdown, CancellationToken.None);

        try
        {
            var generator = new OpenXmlReportDocumentGenerator();
            var resultPath = await generator.GenerateDocxAsync(markdownPath, outputPath, CancellationToken.None);

            Assert.True(File.Exists(resultPath));

            using var document = WordprocessingDocument.Open(resultPath, false);
            var paragraphs = document.MainDocumentPart?.Document.Body?.Elements<Paragraph>().ToList() ?? new List<Paragraph>();
            Assert.NotEmpty(paragraphs);

            Assert.Contains(paragraphs, paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val == "Title");
            Assert.Contains(paragraphs, paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1");
            Assert.Contains(paragraphs, paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2");

            var bulletParagraphs = paragraphs.Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null).ToList();
            Assert.Equal(2, bulletParagraphs.Count);

            var textContent = string.Concat(paragraphs.SelectMany(paragraph => paragraph.Descendants<Text>()).Select(text => text.Text));
            Assert.Contains("Executive Summary", textContent, StringComparison.Ordinal);
            Assert.Contains("Bullet one", textContent, StringComparison.Ordinal);
            Assert.Contains("Bullet two", textContent, StringComparison.Ordinal);
            Assert.Contains("https://contoso.com/docs", textContent, StringComparison.Ordinal);

            var hyperlink = document.MainDocumentPart?.Document.Body?.Descendants<Hyperlink>().FirstOrDefault();
            Assert.NotNull(hyperlink);
            Assert.False(string.IsNullOrWhiteSpace(hyperlink!.Id?.Value));
        }
        finally
        {
            if (File.Exists(markdownPath))
            {
                File.Delete(markdownPath);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public async Task GenerateDocxAsync_ThrowsWhenMarkdownMissing()
    {
        var generator = new OpenXmlReportDocumentGenerator();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.md");
        var outputPath = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.docx");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => generator.GenerateDocxAsync(missingPath, outputPath, CancellationToken.None));

        Assert.Contains("Markdown report not found", exception.Message, StringComparison.Ordinal);
    }
}
