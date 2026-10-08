using System.Buffers;
using System.Text;

namespace AiIntelligence.Application.Reporting;

public static class XmlSafeTextSanitizer
{
    private const int MaxReportedCodePoints = 8;

    public static XmlSafeTextSanitizationResult Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new XmlSafeTextSanitizationResult(value ?? string.Empty, 0, Array.Empty<int>(), false);
        }

        StringBuilder? sanitized = null;
        var reportedCodePoints = new List<int>(MaxReportedCodePoints);
        var codePointsTruncated = false;
        var removedCount = 0;
        var segmentStart = 0;
        var index = 0;

        while (index < value.Length)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var charsConsumed);
            if (status == OperationStatus.Done && IsXml10Valid(rune.Value))
            {
                index += charsConsumed;
                continue;
            }

            sanitized ??= new StringBuilder(value.Length);
            sanitized.Append(value.AsSpan(segmentStart, index - segmentStart));
            var invalidValue = status == OperationStatus.Done ? rune.Value : value[index];
            removedCount++;
            if (!reportedCodePoints.Contains(invalidValue))
            {
                if (reportedCodePoints.Count < MaxReportedCodePoints)
                {
                    reportedCodePoints.Add(invalidValue);
                }
                else
                {
                    codePointsTruncated = true;
                }
            }

            index += status == OperationStatus.Done ? charsConsumed : 1;
            segmentStart = index;
        }

        if (sanitized is null)
        {
            return new XmlSafeTextSanitizationResult(value, 0, Array.Empty<int>(), false);
        }

        sanitized.Append(value.AsSpan(segmentStart));
        return new XmlSafeTextSanitizationResult(sanitized.ToString(), removedCount, reportedCodePoints, codePointsTruncated);
    }

    private static bool IsXml10Valid(int codePoint) =>
        codePoint is 0x9 or 0xA or 0xD
        || codePoint is >= 0x20 and <= 0xD7FF
        || codePoint is >= 0xE000 and <= 0xFFFD
        || codePoint is >= 0x10000 and <= 0x10FFFF;
}

public sealed record XmlSafeTextSanitizationResult(
    string Text,
    int RemovedCharacterCount,
    IReadOnlyList<int> RemovedCodePoints,
    bool CodePointsTruncated);
