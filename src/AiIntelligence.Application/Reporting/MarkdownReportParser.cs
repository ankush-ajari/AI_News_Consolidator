using System.Text.RegularExpressions;

namespace AiIntelligence.Application.Reporting;

public static class MarkdownReportParser
{
    private static readonly Regex LinkRegex = new(@"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BoldRegex = new(@"\*\*(?<text>[^*]+)\*\*", RegexOptions.Compiled);

    public static IEnumerable<MarkdownBlock> Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var pendingParagraph = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                foreach (var block in FlushParagraph(pendingParagraph))
                {
                    yield return block;
                }

                yield return new MarkdownBlankLine();
                continue;
            }

            var trimmedStart = line.TrimStart();

            if (trimmedStart.StartsWith("# ", StringComparison.Ordinal))
            {
                foreach (var block in FlushParagraph(pendingParagraph))
                {
                    yield return block;
                }

                yield return new MarkdownHeading(1, trimmedStart[2..].Trim());
                continue;
            }

            if (trimmedStart.StartsWith("## ", StringComparison.Ordinal))
            {
                foreach (var block in FlushParagraph(pendingParagraph))
                {
                    yield return block;
                }

                yield return new MarkdownHeading(2, trimmedStart[3..].Trim());
                continue;
            }

            if (trimmedStart.StartsWith("### ", StringComparison.Ordinal))
            {
                foreach (var block in FlushParagraph(pendingParagraph))
                {
                    yield return block;
                }

                yield return new MarkdownHeading(3, trimmedStart[4..].Trim());
                continue;
            }

            if (trimmedStart.StartsWith("- ", StringComparison.Ordinal))
            {
                foreach (var block in FlushParagraph(pendingParagraph))
                {
                    yield return block;
                }

                yield return new MarkdownBullet(trimmedStart[2..].Trim());
                continue;
            }

            // Numbered list detection (e.g., "1. item") - treat as ordered list item
            var trimmed2 = trimmedStart;
            var dotIndex = trimmed2.IndexOf('.');
            if (dotIndex > 0 && dotIndex < trimmed2.Length - 1 && int.TryParse(trimmed2[..dotIndex], out _)
                && trimmed2[dotIndex + 1] == ' ')
            {
                foreach (var block in FlushParagraph(pendingParagraph))
                {
                    yield return block;
                }

                yield return new MarkdownNumbered(trimmed2[(dotIndex + 2)..].Trim());
                continue;
            }

            pendingParagraph.Add(line.Trim());
        }

        foreach (var block in FlushParagraph(pendingParagraph))
        {
            yield return block;
        }
    }

    public static IEnumerable<MarkdownInline> ParseInline(string text)
    {
        var remaining = text;
        // Defensive: ensure parser always makes progress. If a special marker
        // character (like '[') appears but does not form a valid link, the
        // previous implementation could return a nextIndex == 0 and yield an
        // empty MarkdownText segment without advancing the remaining string,
        // creating an infinite loop on malformed input. The loop below ensures
        // we always consume at least one character when no valid special token
        // is recognized at position 0.
        while (!string.IsNullOrEmpty(remaining))
        {
            var linkMatch = LinkRegex.Match(remaining);
            if (linkMatch.Success && linkMatch.Index == 0)
            {
                yield return new MarkdownLink(linkMatch.Groups["text"].Value, linkMatch.Groups["url"].Value);
                remaining = remaining[linkMatch.Length..];
                continue;
            }

            var urlMatch = UrlRegex.Match(remaining);
            if (urlMatch.Success && urlMatch.Index == 0)
            {
                var url = urlMatch.Value.TrimEnd('.', ',', ';');
                yield return new MarkdownLink(url, url);
                remaining = remaining[url.Length..];
                continue;
            }

            var boldMatch = BoldRegex.Match(remaining);
            if (boldMatch.Success && boldMatch.Index == 0)
            {
                yield return new MarkdownBold(boldMatch.Groups["text"].Value);
                remaining = remaining[boldMatch.Length..];
                continue;
            }

            var nextIndex = NextSpecialIndex(remaining);

            if (nextIndex < 0)
            {
                // No special token found; emit the remainder and finish.
                yield return new MarkdownText(remaining);
                remaining = string.Empty;
                continue;
            }

            if (nextIndex == 0)
            {
                // A special marker exists at position 0 but none of the token
                // matchers succeeded (malformed input). Consume a single
                // character as literal to guarantee progress and avoid
                // infinite loops.
                yield return new MarkdownText(remaining[0].ToString());
                remaining = remaining.Length > 1 ? remaining[1..] : string.Empty;
                continue;
            }

            var segment = remaining[..nextIndex];
            yield return new MarkdownText(segment);
            remaining = remaining[nextIndex..];
        }
    }

    private static IEnumerable<MarkdownBlock> FlushParagraph(List<string> paragraphLines)
    {
        if (paragraphLines.Count == 0)
        {
            yield break;
        }

        yield return new MarkdownParagraph(string.Join(' ', paragraphLines))
        {
            SourceLineCount = paragraphLines.Count
        };
        paragraphLines.Clear();
    }

    private static int NextSpecialIndex(string text)
    {
        var linkIndex = text.IndexOf('[', StringComparison.Ordinal);
        var urlIndex = UrlRegex.Match(text).Success ? UrlRegex.Match(text).Index : -1;
        var boldIndex = text.IndexOf("**", StringComparison.Ordinal);

        return new[] { linkIndex, urlIndex, boldIndex }
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }
}

public abstract record MarkdownBlock
{
    public int SourceLineCount { get; init; } = 1;
}

public sealed record MarkdownHeading(int Level, string Text) : MarkdownBlock;

public sealed record MarkdownParagraph(string Text) : MarkdownBlock;

public sealed record MarkdownBullet(string Text) : MarkdownBlock;

public sealed record MarkdownBlankLine() : MarkdownBlock;

public sealed record MarkdownNumbered(string Text) : MarkdownBlock;

public abstract record MarkdownInline;

public sealed record MarkdownText(string Text) : MarkdownInline;

public sealed record MarkdownBold(string Text) : MarkdownInline;

public sealed record MarkdownLink(string Text, string Url) : MarkdownInline;
