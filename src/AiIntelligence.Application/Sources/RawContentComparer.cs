using System.Text;
using System.Text.RegularExpressions;

namespace AiIntelligence.Application.Sources;

public static class RawContentComparer
{
    private const double LengthDeltaThreshold = 0.02;
    private const double SimilarityThreshold = 0.985;
    private const int FingerprintLength = 2000;

    public static bool IsMateriallyDifferent(string existingContent, string incomingContent)
    {
        var left = Normalize(existingContent);
        var right = Normalize(incomingContent);

        if (left.Length == 0 && right.Length == 0)
        {
            return false;
        }

        var maxLength = Math.Max(left.Length, right.Length);
        if (maxLength == 0)
        {
            return false;
        }

        var lengthDelta = Math.Abs(left.Length - right.Length) / (double)maxLength;
        if (lengthDelta > LengthDeltaThreshold)
        {
            return true;
        }

        var leftFingerprint = BuildFingerprint(left);
        var rightFingerprint = BuildFingerprint(right);
        var similarity = Jaccard(leftFingerprint, rightFingerprint);

        return similarity < SimilarityThreshold;
    }

    public static string NormalizeCanonicalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return url.Trim();
        }

        var builder = new UriBuilder(parsed)
        {
            Scheme = parsed.Scheme.ToLowerInvariant(),
            Host = parsed.Host.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443)
            || (builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80))
        {
            builder.Port = -1;
        }

        builder.Path = builder.Path.TrimEnd('/');
        builder.Query = NormalizeQuery(builder.Query);

        return builder.Uri.AbsoluteUri;
    }

    private static string Normalize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "\\s+", " ");
        return normalized;
    }

    private static HashSet<string> BuildFingerprint(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var sample = content.Length <= FingerprintLength
            ? content
            : string.Concat(content.AsSpan(0, FingerprintLength), " ", content.AsSpan(content.Length - FingerprintLength));

        var tokens = Regex.Split(sample, "[^a-z0-9]+", RegexOptions.IgnoreCase)
            .Where(token => token.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tokens;
    }

    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1.0;
        }

        var intersection = left.Intersect(right).Count();
        var union = left.Union(right).Count();
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var pairs = trimmed
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Select(parts => new KeyValuePair<string, string>(
                parts[0].Trim().ToLowerInvariant(),
                parts.Length > 1 ? parts[1].Trim() : string.Empty))
            .Where(pair => !IsTrackingKey(pair.Key))
            .OrderBy(pair => pair.Key)
            .ThenBy(pair => pair.Value)
            .ToArray();

        if (pairs.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < pairs.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('&');
            }

            builder.Append(pairs[index].Key);
            if (!string.IsNullOrWhiteSpace(pairs[index].Value))
            {
                builder.Append('=');
                builder.Append(pairs[index].Value);
            }
        }

        return builder.ToString();
    }

    private static bool IsTrackingKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || key is "gclid" or "fbclid" or "msclkid" or "mc_cid" or "mc_eid" or "igshid";
    }
}
