using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Intelligence;

public interface ITrendFamilyClassifier
{
    TrendFamily Classify(string topic, string finding, string evidenceSummary, IReadOnlyCollection<AIConceptTag> conceptTags);
}
