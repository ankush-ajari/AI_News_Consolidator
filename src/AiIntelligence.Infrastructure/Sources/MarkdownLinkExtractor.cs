using System.Text.RegularExpressions;

namespace AiIntelligence.Infrastructure.Sources;

internal static partial class MarkdownLinkExtractor
{
    public static IReadOnlyCollection<Uri> ExtractLinks(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<Uri>();
        }

        var links = new List<Uri>();
        foreach (Match match in MarkdownLinkRegex().Matches(markdown))
        {
            if (Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out var uri))
            {
                links.Add(uri);
            }
        }

        foreach (Match match in BareUrlRegex().Matches(markdown))
        {
            if (Uri.TryCreate(match.Value.TrimEnd(')', '.', ','), UriKind.Absolute, out var uri))
            {
                links.Add(uri);
            }
        }

        return links.DistinctBy(uri => uri.AbsoluteUri).ToArray();
    }

    public static bool IsPrimarilyLinks(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return false;
        }

        var linkTextLength = ExtractLinks(markdown).Sum(uri => uri.AbsoluteUri.Length);
        return linkTextLength > 0 && linkTextLength >= markdown.Trim().Length * 0.4;
    }

    [GeneratedRegex(@"\[[^\]]+\]\((https?://[^\s\)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://[^\s\)]+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRegex();
}
