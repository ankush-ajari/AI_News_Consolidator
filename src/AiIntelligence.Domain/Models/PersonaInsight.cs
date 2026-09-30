using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Domain.Models;

public sealed class PersonaInsight
{
    public PersonaInsight(
        PersonaType personaType,
        PersonaRelevance relevance,
        string headline,
        string whyItMatters,
        string recommendedAttention)
    {
        if (string.IsNullOrWhiteSpace(headline))
        {
            throw new ArgumentException("Headline is required.", nameof(headline));
        }

        if (string.IsNullOrWhiteSpace(whyItMatters))
        {
            throw new ArgumentException("Why it matters is required.", nameof(whyItMatters));
        }

        PersonaType = personaType;
        Relevance = relevance;
        Headline = headline.Trim();
        WhyItMatters = whyItMatters.Trim();
        RecommendedAttention = recommendedAttention.TrimOrEmpty();
    }

    public PersonaType PersonaType { get; }

    public PersonaRelevance Relevance { get; }

    public string Headline { get; }

    public string WhyItMatters { get; }

    public string RecommendedAttention { get; }
}
