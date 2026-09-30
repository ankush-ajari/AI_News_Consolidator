using System.Security.Cryptography;
using System.Text;

namespace AiIntelligence.Application.Content;

public sealed class Sha256ContentHashService : IContentHashService
{
    public string ComputeHash(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = NormalizeContent(content);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeContent(string content) => content.Trim().Replace("\r\n", "\n");
}
