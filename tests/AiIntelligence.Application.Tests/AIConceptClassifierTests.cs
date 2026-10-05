using AiIntelligence.Application.Intelligence;
using AiIntelligence.Domain.Enums;

namespace AiIntelligence.Application.Tests;

public sealed class AIConceptClassifierTests
{
    private readonly IAIConceptClassifier _classifier = new DeterministicAIConceptClassifier();

    [Fact]
    public void HostedAgents_AreTaggedAsAgenticAI()
    {
        var tags = _classifier.Classify(
            "Hosted Agents GA",
            "Agent hosting",
            "Agents",
            "Foundry",
            "Hosted agents now generally available.");

        Assert.Contains(AIConceptTag.AgenticAI, tags);
    }

    [Fact]
    public void Toolboxes_AreTaggedAsToolUse()
    {
        var tags = _classifier.Classify(
            "Toolboxes",
            "Tool orchestration",
            "Toolbox",
            "Foundry",
            "Toolboxes enable tool calling.");

        Assert.Contains(AIConceptTag.ToolUse, tags);
    }

    [Fact]
    public void SpeechLlm_IsTaggedAsSpeechVoiceAndModelCapability()
    {
        var tags = _classifier.Classify(
            "Speech LLM 2607",
            "Speech-to-text",
            "Speech",
            "Azure AI Speech",
            "Model improves multilingual recognition.");

        Assert.Contains(AIConceptTag.SpeechVoice, tags);
        Assert.Contains(AIConceptTag.ModelCapability, tags);
    }

    [Fact]
    public void ModelRouter_IsTaggedAsModelOperations()
    {
        var tags = _classifier.Classify(
            "Model Router",
            "Routing",
            "Model operations",
            "Foundry",
            "Model router improvements for hosted model deployment.");

        Assert.Contains(AIConceptTag.ModelOperations, tags);
    }

    [Fact]
    public void InvestmentReport_IsTaggedAsAIInvestment()
    {
        var tags = _classifier.Classify(
            "AI investment growth",
            "Investment",
            "Funding",
            "AI Index",
            "Venture capital and spend increased.");

        Assert.Contains(AIConceptTag.AIInvestment, tags);
    }

    [Fact]
    public void WorkforceReport_IsTaggedAsWorkforceImpact()
    {
        var tags = _classifier.Classify(
            "Workforce skills shifts",
            "Workforce",
            "Skills",
            "WEF",
            "Upskilling and reskilling trends for AI adoption.");

        Assert.Contains(AIConceptTag.WorkforceImpact, tags);
    }
}
