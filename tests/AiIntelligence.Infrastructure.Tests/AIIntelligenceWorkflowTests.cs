using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.AgentFramework;
using AiIntelligence.Infrastructure.AgentFramework.Tools;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class AIIntelligenceWorkflowTests
{
    [Fact]
    public async Task RunReportAsync_ExecutesDeterministicStages_WhenEnabled()
    {
        var sequence = new List<string>();
        var configuration = BuildConfiguration();
        var ingestTool = Substitute.For<IIngestSourcesTool>();
        var currentTool = Substitute.For<IAnalyzeCurrentIntelligenceTool>();
        var trendTool = Substitute.For<IAnalyzeTrendEvidenceTool>();
        var correlationTool = Substitute.For<ICorrelateCurrentDevelopmentTool>();
        var personaTool = Substitute.For<IGeneratePersonaReportTool>();
        var renderTool = Substitute.For<IRenderReportTool>();
        var intelligenceRepository = Substitute.For<IIntelligenceRepository>();
        var trendEvidenceRepository = Substitute.For<ITrendEvidenceRepository>();
        var intelligenceItems = new[] { CreateIntelligence() };
        intelligenceRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(intelligenceItems);
        trendEvidenceRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<TrendEvidence>());

        ingestTool.ExecuteAsync(Arg.Any<IngestSourcesInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("ingestion");
                return new SourceIngestResult(1, 1, 0, Array.Empty<SourceIngestionFailure>());
            });
        currentTool.ExecuteAsync(Arg.Any<AnalyzeCurrentIntelligenceInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("current-analysis");
                return new IntelligenceAnalysisResult(1, 1, 0, 0, 0, string.Empty, Array.Empty<IntelligenceCandidateDiagnostic>(), Array.Empty<IntelligenceCandidateDiagnostic>(), Array.Empty<IntelligenceAnalysisDiagnostic>());
            });
        trendTool.ExecuteAsync(Arg.Any<AnalyzeTrendEvidenceInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("trend-analysis");
                return new TrendAnalysisResult(1, 1, 0, 0, 0, 0);
            });
        correlationTool.ExecuteAsync(Arg.Any<CorrelateCurrentDevelopmentInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("correlation");
                return new CorrelateCurrentDevelopmentResult(CreateCorrelation(), CreateSelection());
            });
        personaTool.ExecuteAsync(Arg.Any<GeneratePersonaReportInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("persona-report");
                return new ReportDocument(DateTimeOffset.UtcNow, "Period", "Summary", Array.Empty<ReportCurrentDevelopment>(), Array.Empty<ReportTrend>(), Array.Empty<TrendCorrelation>(), Array.Empty<PersonaReportSection>(), Array.Empty<SourceReference>());
            });
        renderTool.Execute(Arg.Any<RenderReportInput>())
            .Returns(call =>
            {
                sequence.Add("render-report");
                return "report";
            });

        var workflow = new AIIntelligenceWorkflow(
            ingestTool,
            currentTool,
            trendTool,
            correlationTool,
            personaTool,
            renderTool,
            intelligenceRepository,
            trendEvidenceRepository,
            configuration);

        var result = await workflow.RunReportAsync(new AIIntelligenceWorkflowOptions(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { "ingestion", "current-analysis", "trend-analysis", "correlation", "persona-report", "render-report" }, sequence);
        Assert.Contains(result.StageResults, stage => stage.Stage == "Ingestion" && stage.Status == WorkflowStageStatus.Completed);
        Assert.Contains(result.StageResults, stage => stage.Stage == "CurrentIntelligence" && stage.Status == WorkflowStageStatus.Completed);
        Assert.Contains(result.StageResults, stage => stage.Stage == "TrendAnalysis" && stage.Status == WorkflowStageStatus.Completed);
        Assert.Contains(result.StageResults, stage => stage.Stage == "Correlation" && stage.Status == WorkflowStageStatus.Completed);
        Assert.Contains(result.StageResults, stage => stage.Stage == "PersonaReport" && stage.Status == WorkflowStageStatus.Completed);
        Assert.Contains(result.StageResults, stage => stage.Stage == "RenderReport" && stage.Status == WorkflowStageStatus.Completed);
    }

    [Fact]
    public async Task RunReportAsync_StopsAfterStageFailure()
    {
        var sequence = new List<string>();
        var configuration = BuildConfiguration();
        var ingestTool = Substitute.For<IIngestSourcesTool>();
        var currentTool = Substitute.For<IAnalyzeCurrentIntelligenceTool>();
        var trendTool = Substitute.For<IAnalyzeTrendEvidenceTool>();
        var correlationTool = Substitute.For<ICorrelateCurrentDevelopmentTool>();
        var personaTool = Substitute.For<IGeneratePersonaReportTool>();
        var renderTool = Substitute.For<IRenderReportTool>();
        var intelligenceRepository = Substitute.For<IIntelligenceRepository>();
        var trendEvidenceRepository = Substitute.For<ITrendEvidenceRepository>();

        ingestTool.ExecuteAsync(Arg.Any<IngestSourcesInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("ingestion");
                return new SourceIngestResult(1, 1, 0, Array.Empty<SourceIngestionFailure>());
            });
        currentTool.ExecuteAsync(Arg.Any<AnalyzeCurrentIntelligenceInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("current-analysis");
                return Task.FromException<IntelligenceAnalysisResult>(new InvalidOperationException("boom"));
            });

        var workflow = new AIIntelligenceWorkflow(
            ingestTool,
            currentTool,
            trendTool,
            correlationTool,
            personaTool,
            renderTool,
            intelligenceRepository,
            trendEvidenceRepository,
            configuration);

        var result = await workflow.RunReportAsync(new AIIntelligenceWorkflowOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(new[] { "ingestion", "current-analysis" }, sequence);
        Assert.Single(result.StageErrors);
        Assert.Equal("CurrentIntelligence", result.StageErrors.Single().Stage);
        Assert.Contains(result.StageResults, stage => stage.Stage == "CurrentIntelligence" && stage.Status == WorkflowStageStatus.Failed);
    }

    [Fact]
    public async Task RunReportAsync_UsesPersistedData_WhenSkippingAnalysis()
    {
        var configuration = BuildConfiguration();
        var ingestTool = Substitute.For<IIngestSourcesTool>();
        var currentTool = Substitute.For<IAnalyzeCurrentIntelligenceTool>();
        var trendTool = Substitute.For<IAnalyzeTrendEvidenceTool>();
        var correlationTool = Substitute.For<ICorrelateCurrentDevelopmentTool>();
        var personaTool = Substitute.For<IGeneratePersonaReportTool>();
        var renderTool = Substitute.For<IRenderReportTool>();
        var intelligenceRepository = Substitute.For<IIntelligenceRepository>();
        var trendEvidenceRepository = Substitute.For<ITrendEvidenceRepository>();
        var intelligenceItems = new[] { CreateIntelligence() };
        var trendEvidence = new[] { CreateTrendEvidence() };
        intelligenceRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(intelligenceItems);
        trendEvidenceRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(trendEvidence);
        correlationTool.ExecuteAsync(Arg.Any<CorrelateCurrentDevelopmentInput>(), Arg.Any<CancellationToken>())
            .Returns(new CorrelateCurrentDevelopmentResult(CreateCorrelation(), CreateSelection()));
        personaTool.ExecuteAsync(Arg.Any<GeneratePersonaReportInput>(), Arg.Any<CancellationToken>())
            .Returns(new ReportDocument(DateTimeOffset.UtcNow, "Period", "Summary", Array.Empty<ReportCurrentDevelopment>(), Array.Empty<ReportTrend>(), Array.Empty<TrendCorrelation>(), Array.Empty<PersonaReportSection>(), Array.Empty<SourceReference>()));
        renderTool.Execute(Arg.Any<RenderReportInput>()).Returns("report");

        var workflow = new AIIntelligenceWorkflow(
            ingestTool,
            currentTool,
            trendTool,
            correlationTool,
            personaTool,
            renderTool,
            intelligenceRepository,
            trendEvidenceRepository,
            configuration);

        var result = await workflow.RunReportAsync(new AIIntelligenceWorkflowOptions
        {
            RunIngestion = false,
            RunCurrentAnalysis = false,
            RunTrendAnalysis = false,
            RunReportGeneration = true
        }, CancellationToken.None);

        Assert.True(result.Success);
        await intelligenceRepository.Received(1).ListAsync(Arg.Any<CancellationToken>());
        await trendEvidenceRepository.ReceivedWithAnyArgs(0).ListAsync(default);
        await currentTool.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await trendTool.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        Assert.Contains(result.StageResults, stage => stage.Stage == "Ingestion" && stage.Status == WorkflowStageStatus.Skipped);
        Assert.Contains(result.StageResults, stage => stage.Stage == "TrendAnalysis" && stage.Status == WorkflowStageStatus.Skipped);
    }

    [Fact]
    public async Task RunReportAsync_SkipsRender_WhenReportGenerationDisabled()
    {
        var sequence = new List<string>();
        var configuration = BuildConfiguration();
        var ingestTool = Substitute.For<IIngestSourcesTool>();
        var currentTool = Substitute.For<IAnalyzeCurrentIntelligenceTool>();
        var trendTool = Substitute.For<IAnalyzeTrendEvidenceTool>();
        var correlationTool = Substitute.For<ICorrelateCurrentDevelopmentTool>();
        var personaTool = Substitute.For<IGeneratePersonaReportTool>();
        var renderTool = Substitute.For<IRenderReportTool>();
        var intelligenceRepository = Substitute.For<IIntelligenceRepository>();
        var trendEvidenceRepository = Substitute.For<ITrendEvidenceRepository>();
        intelligenceRepository.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { CreateIntelligence() });
        correlationTool.ExecuteAsync(Arg.Any<CorrelateCurrentDevelopmentInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sequence.Add("correlation");
                return new CorrelateCurrentDevelopmentResult(CreateCorrelation(), CreateSelection());
            });

        var workflow = new AIIntelligenceWorkflow(
            ingestTool,
            currentTool,
            trendTool,
            correlationTool,
            personaTool,
            renderTool,
            intelligenceRepository,
            trendEvidenceRepository,
            configuration);

        var result = await workflow.RunReportAsync(new AIIntelligenceWorkflowOptions
        {
            RunIngestion = false,
            RunCurrentAnalysis = false,
            RunTrendAnalysis = false,
            RunReportGeneration = false
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { "correlation" }, sequence);
        await personaTool.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        renderTool.DidNotReceiveWithAnyArgs().Execute(default!);
        Assert.Contains(result.StageResults, stage => stage.Stage == "PersonaReport" && stage.Status == WorkflowStageStatus.Skipped);
        Assert.Contains(result.StageResults, stage => stage.Stage == "RenderReport" && stage.Status == WorkflowStageStatus.Skipped);
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:Definitions:0:id"] = Guid.NewGuid().ToString(),
            ["Sources:Definitions:0:name"] = "Official",
            ["Sources:Definitions:0:vendor"] = "Vendor",
            ["Sources:Definitions:0:sourceType"] = "WebPage",
            ["Sources:Definitions:0:sourceClass"] = "CurrentOfficial",
            ["Sources:Definitions:0:url"] = "https://example.com/official",
            ["Sources:Definitions:0:isEnabled"] = "true",
            ["Sources:Definitions:1:id"] = Guid.NewGuid().ToString(),
            ["Sources:Definitions:1:name"] = "Trend",
            ["Sources:Definitions:1:vendor"] = "Vendor",
            ["Sources:Definitions:1:sourceType"] = "WebPage",
            ["Sources:Definitions:1:sourceClass"] = "TrendResearch",
            ["Sources:Definitions:1:url"] = "https://example.com/trend",
            ["Sources:Definitions:1:isEnabled"] = "true"
        }).Build();
    }

    private static IntelligenceItem CreateIntelligence() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Vendor",
        "Topic",
        "Category",
        "Product",
        "Summary",
        Array.Empty<string>(),
        Array.Empty<string>(),
        "Unknown",
        SourceClass.CurrentOfficial,
        DateTimeOffset.UtcNow,
        new Uri("https://example.com/official"));

    private static TrendEvidence CreateTrendEvidence() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Trend",
        "2026",
        TrendEvidencePeriodProvenance.SourceContent,
        "Finding",
        "Summary",
        0.8m,
        new Uri("https://example.com/trend"));

    private static TrendCorrelation CreateCorrelation() => new(
        Guid.NewGuid(),
        "Current",
        "Trend",
        CorrelationRelationship.Supports,
        "Explanation",
        "Evidence",
        0.7m,
        new[] { new Uri("https://example.com/trend") },
        false);

    private static TrendCandidateSelection CreateSelection() => new(
        new[] { CreateTrendEvidence() },
        Array.Empty<TrendCandidateDiagnostic>());
}
