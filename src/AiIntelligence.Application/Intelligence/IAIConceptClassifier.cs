using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Intelligence;

public interface IAIConceptClassifier
{
    IReadOnlyCollection<AIConceptTag> Classify(
        string title,
        string topic,
        string category,
        string productOrFramework,
        string summaryOrFinding);
}
