namespace AiIntelligence.Domain.Models;

public static class CanonicalUrlNormalizer
{
    public static string Normalize(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var builder = new UriBuilder(url)
        {
            Scheme = url.Scheme.ToLowerInvariant(),
            Host = url.Host.ToLowerInvariant(),
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

    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            ? Normalize(parsed)
            : url.Trim();
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

        return string.Join('&', pairs.Select(pair => string.IsNullOrWhiteSpace(pair.Value)
            ? pair.Key
            : $"{pair.Key}={pair.Value}"));
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
