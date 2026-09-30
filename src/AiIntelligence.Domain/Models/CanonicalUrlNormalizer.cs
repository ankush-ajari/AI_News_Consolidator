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

        return builder.Uri.AbsoluteUri;
    }
}
