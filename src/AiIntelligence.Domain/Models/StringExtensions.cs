namespace AiIntelligence.Domain.Models;

internal static class StringExtensions
{
    public static string TrimOrEmpty(this string? value) => value?.Trim() ?? string.Empty;
}
