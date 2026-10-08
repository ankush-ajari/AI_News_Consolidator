using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace AiIntelligence.Application.Reporting;

public sealed class OpenXmlReportDocumentGenerator : IReportDocumentGenerator
{
    private const string DefaultTitle = "AI Technology Intelligence Report";
    private const string DefaultCreator = "AI Intelligence";
    private const string DefaultSubject = "AI technology developments, trends and persona insights";
    private readonly ILogger<OpenXmlReportDocumentGenerator>? _logger;

    public OpenXmlReportDocumentGenerator(ILogger<OpenXmlReportDocumentGenerator>? logger = null)
    {
        _logger = logger;
    }

    public Task<string> GenerateDocxAsync(string markdownPath, string outputPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(markdownPath))
        {
            throw new FileNotFoundException($"Markdown report not found: {markdownPath}", markdownPath);
        }

        var markdown = File.ReadAllText(markdownPath);
        var markdownLineCount = markdown.Split('\n').Length;
        _logger?.LogInformation("DOCX START: InputPath={InputPath}; OutputPath={OutputPath}; MarkdownLength={MarkdownLength}; MarkdownLineCount={MarkdownLineCount}", markdownPath, outputPath, markdown.Length, markdownLineCount);
        _logger?.LogInformation("DOCX: Read Markdown complete");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        _logger?.LogInformation("DOCX: Package created");
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var stylePart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylePart.Styles = BuildStyles();

        ApplyDocumentProperties(document);
        ApplyPageSettings(mainPart.Document.Body!);
        EnsureNumbering(mainPart);
        _logger?.LogInformation("DOCX: Numbering initialized");

        var body = mainPart.Document.Body!;
        _logger?.LogInformation("DOCX: Body generation started");

        var processedLines = 0;
        var createdParagraphs = 0;
        var createdRuns = 0;
        var createdHyperlinks = 0;
        var nextProgressLine = 50;

        foreach (var block in MarkdownReportParser.Parse(markdown))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paragraph? paragraph = null;
            switch (block)
            {
                case MarkdownBlankLine:
                    paragraph = CreateSpacingParagraph();
                    break;
                case MarkdownHeading heading:
                    paragraph = CreateHeadingParagraph(mainPart, heading.Text, heading.Level switch
                    {
                        1 => "Title",
                        2 => "Heading1",
                        _ => "Heading2"
                    }, "Heading");
                    break;
                case MarkdownBullet bullet:
                    paragraph = CreateBulletParagraph(mainPart, bullet.Text);
                    break;
                case MarkdownParagraph paragraphBlock:
                    paragraph = CreateParagraph(mainPart, paragraphBlock.Text);
                    break;
            }
            if (paragraph is not null)
            {
                body.Append(paragraph);
                createdParagraphs++;
                createdRuns += paragraph.Descendants<Run>().Count();
                createdHyperlinks += paragraph.Descendants<Hyperlink>().Count();
            }

            processedLines += block.SourceLineCount;
            while (processedLines >= nextProgressLine)
            {
                _logger?.LogInformation("DOCX Progress: ProcessedLines={ProcessedLineCount}; Paragraphs={CreatedParagraphCount}; Runs={CreatedRunCount}; Hyperlinks={CreatedHyperlinkCount}", processedLines, createdParagraphs, createdRuns, createdHyperlinks);
                nextProgressLine += 50;
            }
        }
        _logger?.LogInformation("DOCX: Body generation complete; ProcessedLines={ProcessedLineCount}; Paragraphs={CreatedParagraphCount}; Runs={CreatedRunCount}; Hyperlinks={CreatedHyperlinkCount}", processedLines, createdParagraphs, createdRuns, createdHyperlinks);
        _logger?.LogInformation("DOCX: Save started");
        mainPart.Document.Save();
        _logger?.LogInformation("DOCX: Save complete");
        _logger?.LogInformation("DOCX END: ProcessedLines={ProcessedLineCount}; Paragraphs={CreatedParagraphCount}; Runs={CreatedRunCount}; Hyperlinks={CreatedHyperlinkCount}", processedLines, createdParagraphs, createdRuns, createdHyperlinks);

        return Task.FromResult(outputPath);
    }

    private void ApplyDocumentProperties(WordprocessingDocument document)
    {
        var properties = document.PackageProperties;
        properties.Title = SanitizeForOpenXml(DefaultTitle, "Metadata");
        properties.Subject = SanitizeForOpenXml(DefaultSubject, "Metadata");
        properties.Creator = SanitizeForOpenXml(DefaultCreator, "Metadata");
    }

    private static void ApplyPageSettings(Body body)
    {
        body.Append(new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin
            {
                Top = 1440,
                Bottom = 1440,
                Left = 1440,
                Right = 1440,
                Header = 720,
                Footer = 720,
                Gutter = 0
            }));
    }

    private Styles BuildStyles()
    {
        var styles = new Styles();
        styles.Append(CreateStyle("Normal", "Normal", 22, false, 240, 160));
        styles.Append(CreateStyle("Title", "Title", 44, true, 240, 320));
        styles.Append(CreateStyle("Heading1", "Heading 1", 32, true, 280, 200));
        styles.Append(CreateStyle("Heading2", "Heading 2", 26, true, 240, 160));
        return styles;
    }

    private Style CreateStyle(string styleId, string name, int fontSize, bool bold, int spacingBefore, int spacingAfter)
    {
        var runProperties = new StyleRunProperties(
            new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos" },
            new FontSize { Val = fontSize.ToString() });
        if (bold)
        {
            runProperties.Append(new Bold());
        }

        return new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = styleId,
            CustomStyle = true,
            StyleName = new StyleName { Val = SanitizeForOpenXml(name, "Style metadata") },
            StyleRunProperties = runProperties,
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = spacingBefore.ToString(), After = spacingAfter.ToString() })
        };
    }

    private Paragraph CreateHeadingParagraph(MainDocumentPart mainPart, string text, string styleId, string context)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        AppendRuns(mainPart, paragraph, text, context);
        return paragraph;
    }

    private Paragraph CreateParagraph(MainDocumentPart mainPart, string text)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Normal" }));
        AppendRuns(mainPart, paragraph, text, "Paragraph");
        return paragraph;
    }

    private Paragraph CreateBulletParagraph(MainDocumentPart mainPart, string text)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = "Normal" },
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 1 })));
        AppendRuns(mainPart, paragraph, text, "Bullet");
        return paragraph;
    }

    private static Paragraph CreateSpacingParagraph() =>
        new(new ParagraphProperties(new SpacingBetweenLines { After = "200" }));

    private void AppendRuns(MainDocumentPart mainPart, Paragraph paragraph, string text, string context)
    {
        foreach (var inline in MarkdownReportParser.ParseInline(text))
        {
            switch (inline)
            {
                case MarkdownLink link:
                    paragraph.Append(CreateHyperlinkRun(mainPart, link.Text, link.Url));
                    break;
                case MarkdownBold bold:
                    paragraph.Append(new Run(new RunProperties(new Bold()), CreateTextNode(bold.Text, context)));
                    break;
                case MarkdownText plain:
                    paragraph.Append(new Run(CreateTextNode(plain.Text, context)));
                    break;
            }
        }
    }

    private void EnsureNumbering(MainDocumentPart mainPart)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Bullet },
                    new LevelText { Val = SanitizeForOpenXml("•", "Numbering") },
                    new LevelJustification { Val = LevelJustificationValues.Left },
                    new ParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
                {
                    LevelIndex = 0
                })
            {
                AbstractNumberId = 1
            },
            new NumberingInstance(new AbstractNumId { Val = 1 })
            {
                NumberID = 1
            });
    }

    private Hyperlink CreateHyperlinkRun(MainDocumentPart mainPart, string displayText, string url)
    {
        var safeUrl = SanitizeForOpenXml(url, "Hyperlink URL");
        var relationship = mainPart.AddHyperlinkRelationship(new Uri(safeUrl), true);
        var run = new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }),
            CreateTextNode(displayText, "Hyperlink"));
        return new Hyperlink(run) { Id = relationship.Id, History = OnOffValue.FromBoolean(true) };
    }

    private Text CreateTextNode(string text, string context)
    {
        var safeText = SanitizeForOpenXml(text, context);
        return new Text(safeText) { Space = SpaceProcessingModeValues.Preserve };
    }

    private string SanitizeForOpenXml(string text, string context)
    {
        var result = XmlSafeTextSanitizer.Sanitize(text);
        if (result.RemovedCharacterCount > 0)
        {
            var codePoints = string.Join(", ", result.RemovedCodePoints.Select(codePoint => $"U+{codePoint:X4}"));
            var truncated = result.CodePointsTruncated ? ", ..." : string.Empty;
            _logger?.LogWarning("DOCX sanitization removed {RemovedCharacterCount} invalid XML character(s): {CodePoints}{Truncated}; Context={Context}", result.RemovedCharacterCount, codePoints, truncated, context);
        }

        return result.Text;
    }
}