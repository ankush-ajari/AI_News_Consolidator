namespace AiIntelligence.Application.Intelligence;

public sealed record IntelligenceExtractionResult(
    bool IsRelevantToAI,
    string Vendor,
    string Topic,
    string Category,
    string ProductOrFramework,
    string Summary,
    IReadOnlyCollection<string> Capabilities,
    IReadOnlyCollection<string> Limitations,
    string ReleaseStage,
    decimal RelevanceScore,
    IReadOnlyCollection<string> EvidenceStatements,
    IReadOnlyCollection<SuggestedPersonaRelevance> SuggestedPersonaRelevance);
