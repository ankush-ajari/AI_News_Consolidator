using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Intelligence;

public sealed class DeterministicAIConceptClassifier : IAIConceptClassifier
{
    private static readonly IReadOnlyDictionary<AIConceptTag, string[]> ConceptKeywords = new Dictionary<AIConceptTag, string[]>
    {
        [AIConceptTag.AgenticAI] = new[] { "agent", "agents", "agentic", "hosted agents", "autonomous agents", "multi-agent" },
        [AIConceptTag.ToolUse] = new[] { "tool calling", "tool use", "toolbox", "tools", "function calling" },
        [AIConceptTag.DeveloperPlatform] = new[] { "sdk", "api", "developer tooling", "dev environment", "coding agent", "framework", "platform" },
        [AIConceptTag.ModelCapability] = new[] { "model", "llm", "reasoning", "benchmark", "model performance", "inference" },
        [AIConceptTag.SpeechVoice] = new[] { "speech", "voice", "speech-to-text", "transcription", "audio", "multilingual recognition" },
        [AIConceptTag.MultimodalAI] = new[] { "image", "video", "audio", "multimodal", "vision" },
        [AIConceptTag.ModelOperations] = new[] { "model router", "routing", "deployment", "hosted model", "serving" },
        [AIConceptTag.Evaluation] = new[] { "evaluation", "benchmark", "testing", "reliability", "regression", "saturation" },
        [AIConceptTag.EnterpriseAdoption] = new[] { "adoption", "enterprise ai", "organizational use", "production adoption" },
        [AIConceptTag.WorkforceImpact] = new[] { "jobs", "workforce", "skills", "upskilling", "reskilling" },
        [AIConceptTag.AIInvestment] = new[] { "investment", "funding", "spend", "venture", "capital" },
        [AIConceptTag.Productivity] = new[] { "productivity", "efficiency", "output gains" },
        [AIConceptTag.LocalEdgeAI] = new[] { "local", "edge", "on-prem", "azure local", "offline" },
        [AIConceptTag.OpenSourceModels] = new[] { "open model", "open-source model", "open weights" },
        [AIConceptTag.ModelCompetition] = new[] { "model ranking", "leaderboards", "closed vs open", "us vs china", "model competition" },
        [AIConceptTag.ResponsibleAI] = new[] { "safety", "fairness", "responsible ai", "transparency" },
        [AIConceptTag.Governance] = new[] { "regulation", "governance", "compliance", "policy" }
    };

    public IReadOnlyCollection<AIConceptTag> Classify(
        string title,
        string topic,
        string category,
        string productOrFramework,
        string summaryOrFinding)
    {
        var text = string.Join(' ', title, topic, category, productOrFramework, summaryOrFinding).ToLowerInvariant();
        var tags = new List<AIConceptTag>();

        foreach (var entry in ConceptKeywords)
        {
            if (entry.Value.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                tags.Add(entry.Key);
            }
        }

        return tags;
    }
}
