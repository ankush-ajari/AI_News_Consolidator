using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Intelligence;

public sealed record SuggestedPersonaRelevance(
    PersonaType PersonaType,
    bool IsRelevant,
    string Rationale);
