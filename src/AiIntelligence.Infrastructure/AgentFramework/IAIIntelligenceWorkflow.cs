namespace AiIntelligence.Infrastructure.AgentFramework;

public interface IAIIntelligenceWorkflow
{
    Task<AIIntelligenceWorkflowResult> RunReportAsync(
        AIIntelligenceWorkflowOptions options,
        CancellationToken cancellationToken);
}
