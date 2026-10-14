using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Infrastructure.AgentFramework.Tools;
using AiIntelligence.Infrastructure.Configuration;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly IReportDocumentGenerator _reportDocumentGenerator;
    private readonly AiIntelligence.Application.Reporting.IReportFilePublisher _reportFilePublisher;
    private readonly ReportPublishingOptions _reportPublishingOptions;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly ITrendEvidenceRepository _trendEvidenceRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIIntelligenceWorkflow> _logger;
    private static readonly Func<(long ManagedHeap, long WorkingSet, long Private)> _getMemoryMetrics = () =>
        (GC.GetTotalMemory(false) / 1024 / 1024, Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024, Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024);

    public AIIntelligenceWorkflow(
        IIngestSourcesTool ingestSourcesTool,
        IAnalyzeCurrentIntelligenceTool currentAnalysisTool,
        IAnalyzeTrendEvidenceTool trendAnalysisTool,
        ICorrelateCurrentDevelopmentTool correlationTool,
        IGeneratePersonaReportTool personaReportTool,
        IRenderReportTool renderReportTool,
        IReportDocumentGenerator reportDocumentGenerator,
        AiIntelligence.Application.Reporting.IReportFilePublisher reportFilePublisher,
        IIntelligenceRepository intelligenceRepository,
        ITrendEvidenceRepository trendEvidenceRepository,
        IConfiguration configuration,
        ILogger<AIIntelligenceWorkflow>? logger = null)
        : this(
              ingestSourcesTool,
              currentAnalysisTool,
              trendAnalysisTool,
              correlationTool,
              personaReportTool,
              renderReportTool,
              reportDocumentGenerator,
              reportFilePublisher,
              Microsoft.Extensions.Options.Options.Create(new ReportPublishingOptions()),
              intelligenceRepository,
              trendEvidenceRepository,
              configuration,
              logger)
    {
    }

    public AIIntelligenceWorkflow(
        IIngestSourcesTool ingestSourcesTool,
        IAnalyzeCurrentIntelligenceTool currentAnalysisTool,
        IAnalyzeTrendEvidenceTool trendAnalysisTool,
        ICorrelateCurrentDevelopmentTool correlationTool,
        IGeneratePersonaReportTool personaReportTool,
        IRenderReportTool renderReportTool,
        IReportDocumentGenerator reportDocumentGenerator,
        AiIntelligence.Application.Reporting.IReportFilePublisher reportFilePublisher,
        Microsoft.Extensions.Options.IOptions<ReportPublishingOptions> reportPublishingOptions,
        IIntelligenceRepository intelligenceRepository,
        ITrendEvidenceRepository trendEvidenceRepository,
        IConfiguration configuration,
        ILogger<AIIntelligenceWorkflow>? logger = null)
    {
        _ingestSourcesTool = ingestSourcesTool;
        _currentAnalysisTool = currentAnalysisTool;
        _trendAnalysisTool = trendAnalysisTool;
        _correlationTool = correlationTool;
        _personaReportTool = personaReportTool;
        _renderReportTool = renderReportTool;
        _reportDocumentGenerator = reportDocumentGenerator;
        _reportFilePublisher = reportFilePublisher;
        _reportPublishingOptions = reportPublishingOptions?.Value ?? new ReportPublishingOptions();
        _intelligenceRepository = intelligenceRepository;
        _trendEvidenceRepository = trendEvidenceRepository;
        _configuration = configuration;
        _logger = logger ?? NullLogger<AIIntelligenceWorkflow>.Instance;
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

            if (options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=WORKFLOW START; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }
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
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> docxHandler = ExecuteDocxAsync;
        Func<WorkflowContextState, IWorkflowContext, CancellationToken, ValueTask<WorkflowContextState>> copyHandler = ExecuteCopyAsync;
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
        var docxExecutor = docxHandler
            .BindAsExecutor<WorkflowContextState, WorkflowContextState>("generate-docx");
        var copyExecutor = copyHandler.BindAsExecutor<WorkflowContextState, WorkflowContextState>("copy-report");
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
            .AddEdge<WorkflowContextState>(renderExecutor, docxExecutor, state => state?.CanContinue == true && state?.Options.RunReportGeneration == true)
            .AddEdge<WorkflowContextState>(renderExecutor, finalizeExecutor, state => state?.CanContinue == false || state?.Options.RunReportGeneration == false)
            .AddEdge<WorkflowContextState>(docxExecutor, copyExecutor, state => state?.CanContinue == true)
            .AddEdge<WorkflowContextState>(docxExecutor, finalizeExecutor, state => state?.CanContinue == false)
            .AddEdge<WorkflowContextState>(copyExecutor, finalizeExecutor, _ => true)
            .WithOutputFrom(finalizeExecutor)
            .WithName("AI Intelligence Workflow")
            .WithDescription("Deterministic orchestration over the AI intelligence pipeline.")
            .Build();

        // Execute stages sequentially for deterministic behavior in tests.
        var state = await ExecuteIngestionAsync(initialState, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecuteCurrentAnalysisAsync(state, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecuteTrendAnalysisAsync(state, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecuteCorrelationAsync(state, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecutePersonaAsync(state, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecuteRenderAsync(state, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecuteDocxAsync(state, null!, cancellationToken).ConfigureAwait(false);
        if (!state.CanContinue) return BuildResult(await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        state = await ExecuteCopyAsync(state, null!, cancellationToken).ConfigureAwait(false);
        var finalState = await FinalizeAsync(state, null!, cancellationToken).ConfigureAwait(false);
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
        // Emit quick diagnostic for tests
        Console.WriteLine($"WORKFLOW: StageErrors.Count={state.StageErrors.Count}");
        var insufficientCount = state.Correlations?.Count(correlation => correlation.Relationship == CorrelationRelationship.InsufficientEvidence) ?? 0;
        var personaCount = state.ReportDocument?.PersonaSections.Count;
        var stageResults = BuildStageResults(state, correlationCount, insufficientCount, personaCount);
        
        // Memory diagnostics are emitted to logs only; they are not merged into
        // the workflow result to avoid retaining diagnostic state in the
        // workflow's historical snapshots.

        if (state.StageErrors.Count > 0)
        {
            // Emit stage errors to console to aid unit-test diagnostics.
            var diag = "DEBUG: Workflow StageErrors (names): " + string.Join(", ", state.StageErrors.Select(e => e.Stage + ":" + e.ErrorType + ":" + e.Message));
            Console.WriteLine(diag);
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), "ai-workflow-stage-errors.log");
                File.WriteAllText(tempPath, diag);
            }
            catch
            {
                // best-effort only
            }
        }

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
            state.DocxReportPath,
            state.StageErrors,
            stageResults);
    }

    private static IReadOnlyCollection<WorkflowStageResult> BuildStageResults(
        WorkflowContextState state,
        int correlationCount,
        int insufficientCount,
        int? personaCount)
    {
        var results = new List<WorkflowStageResult>
        {
            CreateStageResult(
                "Ingestion",
                state.IngestionResult is not null,
                state.Options.RunIngestion,
                state.StageErrors,
                state.IngestionResult is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>
                    {
                        ["Inserted"] = state.IngestionResult.InsertedCount.ToString(),
                        ["Duplicates"] = state.IngestionResult.DuplicateCount.ToString(),
                        ["Failures"] = state.IngestionResult.Failures.Count.ToString()
                    }),
            CreateStageResult(
                "CurrentIntelligence",
                state.CurrentAnalysisResult is not null,
                state.Options.RunCurrentAnalysis,
                state.StageErrors,
                state.CurrentAnalysisResult is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>
                    {
                        ["Processed"] = state.CurrentAnalysisResult.ProcessedCount.ToString(),
                        ["Persisted"] = state.CurrentAnalysisResult.PersistedCount.ToString(),
                        ["Irrelevant"] = state.CurrentAnalysisResult.IrrelevantCount.ToString()
                    }),
            CreateStageResult(
                "TrendAnalysis",
                state.TrendAnalysisResult is not null,
                state.Options.RunTrendAnalysis,
                state.StageErrors,
                state.TrendAnalysisResult is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>
                    {
                        ["Processed"] = state.TrendAnalysisResult.ProcessedCount.ToString(),
                        ["Persisted"] = state.TrendAnalysisResult.PersistedCount.ToString(),
                        ["Skipped"] = state.TrendAnalysisResult.SkippedNonTrendResearchCount.ToString()
                    }),
            CreateStageResult(
                "Correlation",
                state.Correlations is not null,
                true,
                state.StageErrors,
                new Dictionary<string, string>
                {
                    ["Correlations"] = correlationCount.ToString(),
                    ["InsufficientEvidence"] = insufficientCount.ToString()
                }),
            CreateStageResult(
                "PersonaReport",
                state.ReportDocument is not null,
                state.Options.RunReportGeneration,
                state.StageErrors,
                new Dictionary<string, string>
                {
                    ["PersonaSections"] = (personaCount ?? 0).ToString()
                }),
            CreateStageResult(
                "RenderReport",
                !string.IsNullOrWhiteSpace(state.Markdown),
                state.Options.RunReportGeneration,
                state.StageErrors,
                new Dictionary<string, string>
                {
                    ["ReportPath"] = state.ReportPath ?? ""
                }),
            CreateStageResult(
                "GenerateDocx",
                !string.IsNullOrWhiteSpace(state.DocxReportPath),
                state.Options.RunReportGeneration,
                state.StageErrors,
                new Dictionary<string, string>
                {
                    ["DocxReportPath"] = state.DocxReportPath ?? ""
                })
        };

        // Add CopyReport stage result derived from publish result recorded in state
        var copyMetrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copyStatus = WorkflowStageStatus.Skipped;
        if (state.PublishResult is null)
        {
            copyStatus = WorkflowStageStatus.Skipped;
        }
        else if (state.PublishResult.Skipped)
        {
            copyStatus = WorkflowStageStatus.Skipped;
            copyMetrics["Reason"] = "PublishingDisabled";
        }
        else if (state.PublishResult.Success)
        {
            copyStatus = WorkflowStageStatus.Completed;
            copyMetrics["Destination"] = state.PublishResult.DestinationPath ?? string.Empty;
        }
        else
        {
            copyStatus = WorkflowStageStatus.Failed;
            copyMetrics["Error"] = state.PublishResult.ErrorMessage ?? "Unknown";
        }

        results.Add(new WorkflowStageResult("CopyReport", copyStatus, copyMetrics));

        return results;
    }

    private static WorkflowStageResult CreateStageResult(
        string stage,
        bool hasResult,
        bool wasEnabled,
        IReadOnlyCollection<WorkflowStageError> errors,
        IReadOnlyDictionary<string, string> metrics)
    {
        var status = errors.Any(error => string.Equals(error.Stage, stage, StringComparison.OrdinalIgnoreCase))
            ? WorkflowStageStatus.Failed
            : hasResult
                ? WorkflowStageStatus.Completed
                : wasEnabled
                    ? WorkflowStageStatus.Failed
                    : WorkflowStageStatus.Skipped;

        var safeMetrics = metrics
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return new WorkflowStageResult(stage, status, safeMetrics);
    }

    private async ValueTask<WorkflowContextState> ExecuteIngestionAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: Ingestion");
        if (!state.CanContinue || !state.Options.RunIngestion)
        {
            Console.WriteLine("STAGE SKIP: Ingestion");
            return state;
        }

        try
        {
            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=WORKFLOW START; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            var result = await _ingestSourcesTool
                .ExecuteAsync(new IngestSourcesInput(state.Sources), cancellationToken)
                .ConfigureAwait(false);

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=Ingestion; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return state with { IngestionResult = result };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: Ingestion -> {exception.GetType().Name}: {exception.Message}");
            return state.AddStageError("Ingestion", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteCurrentAnalysisAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: CurrentIntelligence");
        if (!state.CanContinue || !state.Options.RunCurrentAnalysis)
        {
            Console.WriteLine("STAGE SKIP: CurrentIntelligence");
            return state;
        }

        try
        {
            var result = await _currentAnalysisTool
                .ExecuteAsync(
                    new AnalyzeCurrentIntelligenceInput(state.Options.CurrentIntelligenceLimit, state.Options.Verbose),
                    cancellationToken)
                .ConfigureAwait(false);

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=CurrentIntelligence; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return state with { CurrentAnalysisResult = result };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: CurrentIntelligence -> {exception.GetType().Name}: {exception.Message}");
            return state.AddStageError("CurrentIntelligence", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteTrendAnalysisAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: TrendAnalysis");
        if (!state.CanContinue || !state.Options.RunTrendAnalysis)
        {
            Console.WriteLine("STAGE SKIP: TrendAnalysis");
            return state;
        }

        try
        {
            var result = await _trendAnalysisTool
                .ExecuteAsync(new AnalyzeTrendEvidenceInput(state.Options.TrendAnalysisLimit), cancellationToken)
                .ConfigureAwait(false);

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=TrendAnalysis; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return state with { TrendAnalysisResult = result };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: TrendAnalysis -> {exception.GetType().Name}: {exception.Message}");
            return state.AddStageError("TrendAnalysis", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteCorrelationAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: Correlation");
        if (!state.CanContinue)
        {
            Console.WriteLine("STAGE SKIP: Correlation");
            return state;
        }

        try
        {
            var selectedSourceIds = state.CurrentAnalysisResult?.SelectedCandidates
                .Select(candidate => candidate.RawSourceItemId)
                .ToHashSet() ?? new HashSet<Guid>();
            var scopedItems = await _intelligenceRepository
                .ListForCorrelationAsync(selectedSourceIds, state.Options.CurrentIntelligenceLimit, cancellationToken)
                .ConfigureAwait(false);

            var correlations = new List<TrendCorrelation>();
            var selectedTrendEvidence = new List<TrendEvidence>();

            foreach (var item in scopedItems)
            {
                var correlationResult = await _correlationTool
                    .ExecuteAsync(new CorrelateCurrentDevelopmentInput(item, DefaultCorrelationCandidateLimit), cancellationToken)
                    .ConfigureAwait(false);
                correlations.Add(correlationResult.Correlation);
                selectedTrendEvidence.AddRange(correlationResult.CandidateSelection.Candidates);
            }

            var distinctEvidence = selectedTrendEvidence.GroupBy(trend => trend.Id).Select(group => group.First()).ToArray();
            var canonicalEvidence = FilterCanonicalTrendEvidence(distinctEvidence);

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=Correlation; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return state with { IntelligenceItems = scopedItems, Correlations = correlations, SelectedTrendEvidence = canonicalEvidence };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: Correlation -> {exception.GetType().Name}: {exception.Message}");
            return state.AddStageError("Correlation", exception.GetType().Name, exception.Message);
        }
    }

    private async ValueTask<WorkflowContextState> ExecutePersonaAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: PersonaReport");
        if (!state.CanContinue || !state.Options.RunReportGeneration)
        {
            Console.WriteLine("STAGE SKIP: PersonaReport");
            return state;
        }

        try
        {
            var intelligenceItems = state.IntelligenceItems?.Count > 0
                ? state.IntelligenceItems
                : await _intelligenceRepository
                    .ListForPersonaAsync(state.Options.CurrentIntelligenceLimit, cancellationToken)
                    .ConfigureAwait(false);
            var correlations = state.Correlations ?? Array.Empty<TrendCorrelation>();
            var trendEvidence = state.SelectedTrendEvidence ?? await GetTrendEvidenceForCorrelationsAsync(correlations, cancellationToken).ConfigureAwait(false);

            var document = await _personaReportTool
                .ExecuteAsync(new GeneratePersonaReportInput(intelligenceItems, correlations, trendEvidence), cancellationToken)
                .ConfigureAwait(false);

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=PersonaReport; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return state with { ReportDocument = document };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: PersonaReport -> {exception.GetType().Name}: {exception.Message}");
            return state.AddStageError("PersonaReport", exception.GetType().Name, exception.Message);
        }
    }

    private ValueTask<WorkflowContextState> ExecuteRenderAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: RenderReport");
        if (!state.CanContinue || !state.Options.RunReportGeneration)
        {
            Console.WriteLine("STAGE SKIP: RenderReport");
            return ValueTask.FromResult(state);
        }

        if (state.ReportDocument is null)
        {
            return ValueTask.FromResult(state.AddStageError("RenderReport", "MissingReport", "Report document was not generated."));
        }

        try
        {
            var markdown = _renderReportTool.Execute(new RenderReportInput(state.ReportDocument));
            var outputDirectory = Path.Combine(Environment.CurrentDirectory, "output");
            Directory.CreateDirectory(outputDirectory);
            var outputFile = Path.Combine(outputDirectory, "ai-intelligence-report.md");
            File.WriteAllText(outputFile, markdown);

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=RenderReport; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return ValueTask.FromResult(state with { Markdown = markdown, ReportPath = outputFile });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: RenderReport -> {exception.GetType().Name}: {exception.Message}");
            return ValueTask.FromResult(state.AddStageError("RenderReport", exception.GetType().Name, exception.Message));
        }
    }

    private async ValueTask<WorkflowContextState> ExecuteDocxAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: GenerateDocx");
        if (!state.CanContinue || !state.Options.RunReportGeneration)
        {
            Console.WriteLine("STAGE SKIP: GenerateDocx");
            return state;
        }

        if (string.IsNullOrWhiteSpace(state.ReportPath))
        {
            return state.AddStageError("GenerateDocx", "MissingReport", "Markdown report was not generated.");
        }

        try
        {
            var outputDirectory = Path.Combine(Environment.CurrentDirectory, "output");
            Directory.CreateDirectory(outputDirectory);
            var outputFile = Path.Combine(outputDirectory, "ai-intelligence-report.docx");
            var genTask = _reportDocumentGenerator.GenerateDocxAsync(state.ReportPath, outputFile, cancellationToken);
            if (genTask is not null)
            {
                await genTask.ConfigureAwait(false);
            }
            else
            {
                // Some test doubles may return null for a Task-returning method; treat as a completed operation.
                await Task.CompletedTask.ConfigureAwait(false);
            }

            if (state.Options.MemoryDiagnostics)
            {
                var (mHeap, wSet, pMem) = _getMemoryMetrics();
                _logger.LogInformation("Memory: Stage=GenerateDocx; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
            }

            return state with { DocxReportPath = outputFile };
        }
        catch (Exception exception)
        {
            Console.WriteLine($"STAGE ERROR: GenerateDocx -> {exception.GetType().Name}: {exception.Message}");
            return state.AddStageError("GenerateDocx", exception.GetType().Name, exception.Message);
        }
    }

    private ValueTask<WorkflowContextState> FinalizeAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (state.Options.MemoryDiagnostics)
        {
            var (mHeap, wSet, pMem) = _getMemoryMetrics();
            _logger.LogInformation("Memory: Stage=WORKFLOW COMPLETE; ManagedHeapMB={ManagedHeapMB}; WorkingSetMB={WorkingSetMB}; PrivateMemoryMB={PrivateMemoryMB}", mHeap, wSet, pMem);
        }

        return ValueTask.FromResult(state);
    }

    private async ValueTask<WorkflowContextState> ExecuteCopyAsync(
        WorkflowContextState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("STAGE START: CopyReport");
        if (!state.CanContinue || !state.Options.RunReportGeneration)
        {
            Console.WriteLine("STAGE SKIP: CopyReport");
            return state;
        }

        if (string.IsNullOrWhiteSpace(state.DocxReportPath))
        {
            // Nothing to copy; upstream did not produce a docx.
            return state;
        }

        try
        {
            // Respect global publishing options: if publishing is disabled, record skipped result.
            if (!_reportPublishingOptions.Enabled)
            {
                var skipped = new AiIntelligence.Application.Reporting.ReportPublishResult(false, state.DocxReportPath, null, "PublishingDisabled", true);
                return state with { PublishResult = skipped };
            }

            if (!File.Exists(state.DocxReportPath))
            {
                return state.AddStageError("CopyReport", "SourceMissing", "Docx report file missing at expected path.");
            }

            var publishResult = await _reportFilePublisher.PublishDocxAsync(state.DocxReportPath, cancellationToken).ConfigureAwait(false);
            if (!publishResult.Success && !publishResult.Skipped)
            {
                return state.AddStageError("CopyReport", "PublishFailed", publishResult.ErrorMessage ?? "Publish failed");
            }

            return state with { PublishResult = publishResult };
        }
        catch (Exception exception)
        {
            return state.AddStageError("CopyReport", exception.GetType().Name, exception.Message);
        }
    }

    private static IReadOnlyCollection<IntelligenceItem> ApplyLimit(
        IReadOnlyCollection<IntelligenceItem> items,
        int? limit)
    {
        return limit is > 0
            ? items.Take(limit.Value).ToArray()
            : items.ToArray();
    }

    private static IReadOnlyCollection<IntelligenceItem> FilterMockIntelligenceItems(
        IReadOnlyCollection<IntelligenceItem> items)
    {
        return items.Where(item => !item.Summary.Contains("Mock", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Topic, "AI technology development", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IReadOnlyCollection<TrendEvidence> FilterCanonicalTrendEvidence(
        IReadOnlyCollection<TrendEvidence> evidence)
    {
        if (evidence.Count == 0)
        {
            return evidence;
        }

        var detector = new TrendEvidenceDuplicateDetector();
        var output = new List<TrendEvidence>();

        foreach (var sourceGroup in evidence.GroupBy(item => item.SourceItemId))
        {
            var candidates = sourceGroup
                .Select(item => new
                {
                    Trend = item,
                    Candidate = detector.CreateCandidate(item, Guid.Empty, item.TrendFamily)
                })
                .OrderByDescending(entry => ScoreCandidateSpecificity(entry.Candidate))
                .ToList();

            var accepted = new List<TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate>();
            var acceptedEvidence = new List<TrendEvidence>();

            foreach (var entry in candidates)
            {
                if (detector.TryMergeCandidate(entry.Candidate, accepted, out _))
                {
                    continue;
                }

                accepted.Add(entry.Candidate);
                acceptedEvidence.Add(entry.Trend);
            }

            output.AddRange(acceptedEvidence);
        }

        return output
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .ToArray();

        static int ScoreCandidateSpecificity(TrendEvidenceDuplicateDetector.NormalizedTrendEvidenceCandidate candidate)
        {
            var findingScore = candidate.RawFinding?.Length ?? 0;
            var summaryScore = candidate.RawEvidenceSummary?.Length ?? 0;
            var periodScore = ScorePeriodSpecificity(candidate.BestPeriod, candidate.BestPeriodProvenance);
            return (findingScore * 3) + (summaryScore * 2) + periodScore;
        }

        static int ScorePeriodSpecificity(string period, TrendEvidencePeriodProvenance provenance)
        {
            if (!string.IsNullOrWhiteSpace(period)
                && !string.Equals(period, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return provenance switch
                {
                    TrendEvidencePeriodProvenance.SourceContent => 4,
                    TrendEvidencePeriodProvenance.SourceMetadata => 3,
                    TrendEvidencePeriodProvenance.PublicationDate => 2,
                    _ => 1
                };
            }

            return 0;
        }
    }

    private async Task<IReadOnlyCollection<TrendEvidence>> GetTrendEvidenceForCorrelationsAsync(
        IReadOnlyCollection<TrendCorrelation> correlations,
        CancellationToken cancellationToken)
    {
        if (correlations.Count == 0)
        {
            return Array.Empty<TrendEvidence>();
        }

        var sourceUrls = correlations.SelectMany(correlation => correlation.SupportingSourceUrls)
            .Where(url => url is not null)
            .Select(url => url!.AbsoluteUri)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sourceUrls.Count == 0)
        {
            return Array.Empty<TrendEvidence>();
        }

        // Use repository API to fetch only the trend evidence matching the
        // supporting source URLs. This avoids fetching the entire table into
        // memory.
        var evidence = await _trendEvidenceRepository.GetBySourceUrlsAsync(sourceUrls.ToArray(), cancellationToken).ConfigureAwait(false);
        return evidence.ToArray();
    }
}
