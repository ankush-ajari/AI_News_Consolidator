using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Infrastructure.AgentFramework.Tools;
using AiIntelligence.Infrastructure.Configuration;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using Microsoft.Extensions.Configuration;

namespace AiIntelligence.Infrastructure.AgentFramework;

public sealed class AIIntelligenceWorkflow : IAIIntelligenceWorkflow
{
    private const int DefaultCorrelationCandidateLimit = 5;

    private readonly IIngestSourcesTool _ingestSourcesTool;
    private readonly IAnalyzeCurrentIntelligenceTool _currentAnalysisTool;
    private readonly IAnalyzeTrendEvidenceTool _trendAnalysisTool;
    private readonly ICorrelateCurrentDevelopmentTool _correlationTool;
    private readonly IGeneratePersonaReportTool _personaReportTool;
    private readonly IRenderReportTool _renderReportTool;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly ITrendEvidenceRepository _trendEvidenceRepository;
    private readonly IConfiguration _configuration;

    public AIIntelligenceWorkflow(
        IIngestSourcesTool ingestSourcesTool,
        IAnalyzeCurrentIntelligenceTool currentAnalysisTool,
        IAnalyzeTrendEvidenceTool trendAnalysisTool,
        ICorrelateCurrentDevelopmentTool correlationTool,
        IGeneratePersonaReportTool personaReportTool,
        IRenderReportTool renderReportTool,
        IIntelligenceRepository intelligenceRepository,
        ITrendEvidenceRepository trendEvidenceRepository,
        IConfiguration configuration)
    {
        _ingestSourcesTool = ingestSourcesTool;
        _currentAnalysisTool = currentAnalysisTool;
        _trendAnalysisTool = trendAnalysisTool;
        _correlationTool = correlationTool;
        _personaReportTool = personaReportTool;
        _renderReportTool = renderReportTool;
        _intelligenceRepository = intelligenceRepository;
        _trendEvidenceRepository = trendEvidenceRepository;
        _configuration = configuration;
    }

    public async Task<AIIntelligenceWorkflowResult> RunReportAsync(
        AIIntelligenceWorkflowOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        WorkflowContextState initialState;
        try
        {
            var sources = SourceDefinitionConfigurationLoader.Load(_configuration);
            initialState = WorkflowContextState.Create(DateTimeOffset.UtcNow, options, sources);
        }
        catch (Exception exception)
        {
            var fallback = WorkflowContextState.Create(DateTimeOffset.UtcNow, options, Array.Empty<SourceDefinition>())
                .AddStageError("Configuration", exception.GetType().Name, exception.Message);
            return BuildResult(fallback, DateTimeOffset.UtcNow);
        }

        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> ingestionHandler = ExecuteIngestionAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> currentHandler = ExecuteCurrentAnalysisAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> trendHandler = ExecuteTrendAnalysisAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> correlationHandler = ExecuteCorrelationAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> personaHandler = ExecutePersonaAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> renderHandler = ExecuteRenderAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> finalizeHandler = FinalizeAsync;

        var ingestionExecutor = ingestionHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("ingestion");
        var currentExecutor = currentHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("current-analysis");
        var trendExecutor = trendHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("trend-analysis");
        var correlationExecutor = correlationHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("correlation");
        var personaExecutor = personaHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("persona-report");
        var renderExecutor = renderHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("render-report");
        var finalizeExecutor = finalizeHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("finalize");

        var workflow = new WorkflowBuilder(ingestionExecutor)
            .AddEdge<WorkflowContextState>(ingestionExecutor, currentExecutor, state => state?.CanContinue == true)
            .AddEdge<WorkflowContextState>(ingestionExecutor, finalizeExecutor, state => state?.CanContinue == false)
            .AddEdge<WorkflowContextState>(currentExecutor, trendExecutor, state => state?.CanContinue == true)
            .AddEdge<WorkflowContextState>(currentExecutor, finalizeExecutor, state => state?.CanContinue == false)
            .AddEdge<WorkflowContextState>(trendExecutor, correlationExecutor, state => state?.CanContinue == true)
            .AddEdge<WorkflowContextState>(trendExecutor, finalizeExecutor, state => state?.CanContinue == false)
            .AddEdge<WorkflowContextState>(correlationExecutor, personaExecutor, state => state?.CanContinue == true && state?.Options.RunReportGeneration == true)
            .AddEdge<WorkflowContextState>(correlationExecutor, finalizeExecutor, state => state?.CanContinue == false || state?.Options.RunReportGeneration == false)
            .AddEdge<WorkflowContextState>(personaExecutor, renderExecutor, state => state?.CanContinue == true && state?.Options.RunReportGeneration == true)
            .AddEdge<WorkflowContextState>(personaExecutor, finalizeExecutor, state => state?.CanContinue == false || state?.Options.RunReportGeneration == false)
            .AddEdge<WorkflowContextState>(renderExecutor, finalizeExecutor, _ => true)
            .WithOutputFrom(finalizeExecutor)
            .WithName("AI Intelligence Workflow")
            .WithDescription("Deterministic orchestration over the AI intelligence pipeline.")
            .Build();

        var run = await InProcessExecution.Default
            .RunAsync(workflow, initialState, sessionId: null, cancellationToken)
            .ConfigureAwait(false);

        var finalState = ExtractOutputState(run) ?? initialState.AddStageError("Finalize", "MissingOutput", "Workflow did not yield a final output.");
        return BuildResult(finalState, DateTimeOffset.UtcNow);
    }

    private static WorkflowContextState? ExtractOutputState(Run run)
    {
        foreach (var eventItem in run.OutgoingEvents)
        {
            if (eventItem is WorkflowOutputEvent outputEvent
                && outputEvent.Is<WorkflowContextState>(out var outputState))
            {
                return outputState;
            }
        }

        return null;
    }

    private static AIIntelligenceWorkflowResult BuildResult(WorkflowContextState state, DateTimeOffset completedAt)
    {
        var correlationCount = state.Correlations?.Count ?? 0;
        var insufficientCount = state.Correlations?.Count(correlation => correlation.Relationship == CorrelationRelationship.InsufficientEvidence) ?? 0;
        var personaCount = state.ReportDocument?.PersonaSections.Count;

        return new AIIntelligenceWorkflowResult(
            state.StartedAt,
            completedAt,
            state.StageErrors.Count == 0,
            state.IngestionResult is null
                ? null
                : $"Fetched: {state.IngestionResult.FetchedCount}; Inserted: {state.IngestionResult.InsertedCount}; Duplicates: {state.IngestionResult.DuplicateCount}; Failures: {state.IngestionResult.Failures.Count}",
            state.CurrentAnalysisResult is null
                ? null
                : $"Processed: {state.CurrentAnalysisResult.ProcessedCount}; Persisted: {state.CurrentAnalysisResult.PersistedCount}; Irrelevant: {state.CurrentAnalysisResult.IrrelevantCount}; Skipped: {state.CurrentAnalysisResult.SkippedNonCurrentOfficialCount}; Failed: {state.CurrentAnalysisResult.FailedCount}",
            state.TrendAnalysisResult is null
                ? null
                : $"Processed: {state.TrendAnalysisResult.ProcessedCount}; Persisted: {state.TrendAnalysisResult.PersistedCount}; Skipped: {state.TrendAnalysisResult.SkippedNonTrendResearchCount}; Failed: {state.TrendAnalysisResult.FailedCount}",
            correlationCount == 0 ? null : correlationCount,
            insufficientCount == 0 ? null : insufficientCount,
            personaCount,
            state.ReportPath,
            state.StageErrors);
    }

    private async ValueTask<WorkflowContextState> ExecuteIngestionAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!state.CanContinue || !state.Options.RunIngestion)
        {
            return state;
        }

        try
        {
            var result = await _ingestSourcesTool
                .ExecuteAsync(new IngestSourcesInput(state.Sources), cancellationToken)
                .ConfigureAwait(false);
            return state with { IngestionResult = result };
        }
        catch (Exception exception)
        {
            return state.AddStageError("Ingestion", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteCurrentAnalysisAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!state.CanContinue || !state.Options.RunCurrentAnalysis)
        {
            return state;
        }

        try
        {
            var result = await _currentAnalysisTool
                .ExecuteAsync(
                    new AnalyzeCurrentIntelligenceInput(state.Options.CurrentIntelligenceLimit, state.Options.Verbose),
                    cancellationToken)
                .ConfigureAwait(false);
            return state with { CurrentAnalysisResult = result };
        }
        catch (Exception exception)
        {
            return state.AddStageError("CurrentIntelligence", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteTrendAnalysisAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!state.CanContinue || !state.Options.RunTrendAnalysis)
        {
            return state;
        }

        try
        {
            var result = await _trendAnalysisTool
                .ExecuteAsync(new AnalyzeTrendEvidenceInput(state.Options.TrendAnalysisLimit), cancellationToken)
                .ConfigureAwait(false);
            return state with { TrendAnalysisResult = result };
        }
        catch (Exception exception)
        {
            return state.AddStageError("TrendAnalysis", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteCorrelationAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!state.CanContinue)
        {
            return state;
        }

        try
        {
            var intelligenceItems = await _intelligenceRepository.ListAsync(cancellationToken).ConfigureAwait(false);
            var limitedItems = ApplyLimit(intelligenceItems, state.Options.CurrentIntelligenceLimit);
            var correlations = new List<TrendCorrelation>();

            foreach (var item in limitedItems)
            {
                var correlationResult = await _correlationTool
                    .ExecuteAsync(new CorrelateCurrentDevelopmentInput(item, DefaultCorrelationCandidateLimit), cancellationToken)
                    .ConfigureAwait(false);
                correlations.Add(correlationResult.Correlation);
            }

            return state with { IntelligenceItems = limitedItems, Correlations = correlations };
        }
        catch (Exception exception)
        {
            return state.AddStageError("Correlation", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecutePersonaAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!state.CanContinue || !state.Options.RunReportGeneration)
        {
            return state;
        }

        try
        {
            var intelligenceItems = state.IntelligenceItems?.Count > 0
                ? state.IntelligenceItems
                : ApplyLimit(await _intelligenceRepository.ListAsync(cancellationToken).ConfigureAwait(false), state.Options.CurrentIntelligenceLimit);
            var correlations = state.Correlations ?? Array.Empty<TrendCorrelation>();
            var trendEvidence = await _trendEvidenceRepository.ListAsync(cancellationToken).ConfigureAwait(false);

            var document = await _personaReportTool
                .ExecuteAsync(new GeneratePersonaReportInput(intelligenceItems, correlations, trendEvidence), cancellationToken)
                .ConfigureAwait(false);

            return state with { ReportDocument = document };
        }
        catch (Exception exception)
        {
            return state.AddStageError("PersonaReport", exception.GetType().Name, exception.Message);
        }
    }

    private ValueTask<WorkflowContextState> ExecuteRenderAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!state.CanContinue || !state.Options.RunReportGeneration)
        {
            return ValueTask.FromResult(state);
        }

        if (state.ReportDocument is null)
        {
            return ValueTask.FromResult(state.AddStageError("RenderReport", "MissingReport", "Report document was not generated."));
        }

        try
        {
            var markdown = _renderReportTool.Execute(new RenderReportInput(state.ReportDocument));
            return ValueTask.FromResult(state with { Markdown = markdown });
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(state.AddStageError("RenderReport", exception.GetType().Name, exception.Message));
        }
    }

    private static ValueTask<WorkflowContextState> FinalizeAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(state);
    }

    private static IReadOnlyCollection<IntelligenceItem> ApplyLimit(
        IReadOnlyCollection<IntelligenceItem> items,
        int? limit)
    {
        return limit is > 0
            ? items.Take(limit.Value).ToArray()
            : items.ToArray();
    }
}
