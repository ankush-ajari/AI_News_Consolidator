using AiIntelligence.Application.Content;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;

namespace AiIntelligence.Infrastructure.Sources;

internal static class RawSourceItemFactory
{
    public static RawSourceItem Create(
        IContentHashService contentHashService,
        SourceDefinition sourceDefinition,
        string title,
        Uri url,
        DateTimeOffset? publishedAt,
        DateTimeOffset fetchedAt,
        string rawContent,
        string? enrichedContent = null,
        IReadOnlyCollection<Uri>? contentSourceUrls = null)
    {
        var normalizedContent = rawContent ?? string.Empty;
        var normalizedEnrichedContent = enrichedContent ?? string.Empty;
        var hashInput = string.Concat(normalizedContent, "\n--- enriched ---\n", normalizedEnrichedContent);

        return new RawSourceItem(
            Guid.NewGuid(),
            sourceDefinition.Id,
            title,
            url,
            publishedAt,
            fetchedAt,
            normalizedContent,
            new ContentHash(contentHashService.ComputeHash(hashInput)),
            normalizedEnrichedContent,
            contentSourceUrls,
            sourceDefinition.SourceClass);
    }
}
