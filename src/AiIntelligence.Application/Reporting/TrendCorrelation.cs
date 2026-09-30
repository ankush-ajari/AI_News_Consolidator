namespace AiIntelligence.Application.Reporting;

public sealed record TrendCorrelation(
    Guid IntelligenceItemId,
    string CurrentDevelopment,
    string RelatedTrend,
    CorrelationRelationship Relationship,
    string Explanation,
    string EvidenceBasis,
    decimal Confidence,
    IReadOnlyCollection<Uri> SupportingSourceUrls,
    bool SameSourceEvidence = false);
