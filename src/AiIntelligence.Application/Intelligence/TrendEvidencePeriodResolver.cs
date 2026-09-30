using System.Text.RegularExpressions;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;

namespace AiIntelligence.Application.Intelligence;

public static class TrendEvidencePeriodResolver
{
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

    public static TrendEvidencePeriodResolution Resolve(
        TrendEvidenceExtractionResult extraction,
        RawSourceItem sourceItem,
        SourceDefinition? sourceDefinition)
    {
        if (!string.IsNullOrWhiteSpace(extraction.Period) && !IsUnknown(extraction.Period))
        {
            return new TrendEvidencePeriodResolution(extraction.Period.Trim(), TrendEvidencePeriodProvenance.SourceContent);
        }

        if (sourceItem.PublishedAt.HasValue)
        {
            return new TrendEvidencePeriodResolution(sourceItem.PublishedAt.Value.Year.ToString(), TrendEvidencePeriodProvenance.PublicationDate);
        }

        var metadataUrlYear = ExtractYear(sourceDefinition?.Url?.AbsoluteUri);
        if (!string.IsNullOrWhiteSpace(metadataUrlYear))
        {
            return new TrendEvidencePeriodResolution(metadataUrlYear!, TrendEvidencePeriodProvenance.SourceMetadata);
        }

        var metadataNameYear = ExtractYear(sourceDefinition?.Name);
        if (!string.IsNullOrWhiteSpace(metadataNameYear))
        {
            return new TrendEvidencePeriodResolution(metadataNameYear!, TrendEvidencePeriodProvenance.SourceMetadata);
        }

        return new TrendEvidencePeriodResolution("Unknown", TrendEvidencePeriodProvenance.Unknown);
    }

    private static string? ExtractYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = YearRegex.Match(value);
        return match.Success ? match.Value : null;
    }

    private static bool IsUnknown(string value)
    {
        return value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TrendEvidencePeriodResolution(string Period, TrendEvidencePeriodProvenance Provenance);
