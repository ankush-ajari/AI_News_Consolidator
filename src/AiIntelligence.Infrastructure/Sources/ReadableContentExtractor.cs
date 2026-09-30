using System.Net;
using System.Text.RegularExpressions;

namespace AiIntelligence.Infrastructure.Sources;

internal static partial class ReadableContentExtractor
{
    public static string Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var body = ExtractElement(html, "article")
            ?? ExtractElement(html, "main")
            ?? ExtractElement(html, "body")
            ?? html;

        body = ScriptOrStyleRegex().Replace(body, " ");
        body = TagRegex().Replace(body, " ");
        body = WebUtility.HtmlDecode(body);
        body = WhitespaceRegex().Replace(body, " ").Trim();

        return body;
    }

    private static string? ExtractElement(string html, string tagName)
    {
        var pattern = $"<\\s*{tagName}[^>]*>(.*?)<\\s*/\\s*{tagName}\\s*>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"<\s*(script|style)[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
