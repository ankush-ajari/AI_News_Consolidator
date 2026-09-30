using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using AiIntelligence.Domain.ValueObjects;
using AiIntelligence.Infrastructure.AgentFramework.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AiIntelligence.Infrastructure.Tests;

public sealed class AgentFrameworkToolsTests
{
    [Fact]
    public async Task AnalyzeCurrentIntelligenceTool_DelegatesToService()
    {
        var rawRepository = Substitute.For<Application.Persistence.IRawSourceRepository>();
        var sourceRepository = Substitute.For<Application.Persistence.ISourceDefinitionRepository>();
        var intelligenceRepository = Substitute.For<Application.Persistence.IIntelligenceRepository>();
        var extractor = Substitute.For<IIntelligenceExtractor>();
        var analysisService = new IntelligenceAnalysisService(
            rawRepository,
            sourceRepository,
            intelligenceRepository,
            extractor,
            NullLogger<IntelligenceAnalysisService>.Instance);
        var expected = new IntelligenceAnalysisResult(
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            Array.Empty<IntelligenceCandidateDiagnostic>(),
            Array.Empty<IntelligenceCandidateDiagnostic>(),
            Array.Empty<IntelligenceAnalysisDiagnostic>());
        rawRepository.ListUnprocessedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<RawSourceItem>>(Array.Empty<RawSourceItem>()));
        intelligenceRepository.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var tool = new AnalyzeCurrentIntelligenceTool(analysisService);

        var result = await tool.ExecuteAsync(new AnalyzeCurrentIntelligenceInput(3, true), CancellationToken.None);

        Assert.Equal(expected.ProcessedCount, result.ProcessedCount);
        Assert.Equal(expected.PersistedCount, result.PersistedCount);
        Assert.Equal(expected.IrrelevantCount, result.IrrelevantCount);
        Assert.Equal(expected.FailedCount, result.FailedCount);
        Assert.Equal(expected.SkippedNonCurrentOfficialCount, result.SkippedNonCurrentOfficialCount);
        await rawRepository.Received(1).ListUnprocessedAsync(Arg.Any<CancellationToken>());
        await intelligenceRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnalyzeTrendEvidenceTool_DelegatesToService()
    {
        var rawRepository = Substitute.For<Application.Persistence.IRawSourceRepository>();
        var sourceRepository = Substitute.For<Application.Persistence.ISourceDefinitionRepository>();
        var trendRepository = Substitute.For<Application.Persistence.ITrendEvidenceRepository>();
        var extractor = Substitute.For<ITrendEvidenceExtractor>();
        var trendService = new TrendAnalysisService(
            rawRepository,
            sourceRepository,
            trendRepository,
            extractor,
            NullLogger<TrendAnalysisService>.Instance);
        var expected = new TrendAnalysisResult(0, 0, 0, 0);
        rawRepository.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<RawSourceItem>>(Array.Empty<RawSourceItem>()));
        trendRepository.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var tool = new AnalyzeTrendEvidenceTool(trendService);

        var result = await tool.ExecuteAsync(new AnalyzeTrendEvidenceInput(2), CancellationToken.None);

        Assert.Equal(expected.ProcessedCount, result.ProcessedCount);
        Assert.Equal(expected.PersistedCount, result.PersistedCount);
        Assert.Equal(expected.SkippedNonTrendResearchCount, result.SkippedNonTrendResearchCount);
        Assert.Equal(expected.FailedCount, result.FailedCount);
        await rawRepository.Received(1).ListAsync(Arg.Any<CancellationToken>());
        await trendRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrelateCurrentDevelopmentTool_DelegatesToSelectorAndCorrelation()
    {
        var selector = Substitute.For<ITrendCandidateSelector>();
        var correlationService = Substitute.For<ITrendCorrelationService>();
        var intelligence = CreateIntelligence();
        var selection = new TrendCandidateSelection(Array.Empty<TrendEvidence>(), Array.Empty<TrendCandidateDiagnostic>());
        var correlation = new TrendCorrelation(
            Guid.NewGuid(),
            "current",
            "trend",
            CorrelationRelationship.InsufficientEvidence,
            "explanation",
            "Evidence",
            0m,
            new[] { new Uri("https://example.com") },
            false);
        selector.SelectCandidatesAsync(intelligence, 5, Arg.Any<CancellationToken>()).Returns(selection);
        correlationService.CorrelateAsync(intelligence, selection.Candidates, Arg.Any<CancellationToken>()).Returns(correlation);
        var tool = new CorrelateCurrentDevelopmentTool(selector, correlationService);

        var result = await tool.ExecuteAsync(new CorrelateCurrentDevelopmentInput(intelligence, 5), CancellationToken.None);

        Assert.Same(correlation, result.Correlation);
        Assert.Same(selection, result.CandidateSelection);
        await selector.Received(1).SelectCandidatesAsync(intelligence, 5, Arg.Any<CancellationToken>());
        await correlationService.Received(1).CorrelateAsync(intelligence, selection.Candidates, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GeneratePersonaReportTool_DelegatesToGenerator()
    {
        var generator = Substitute.For<IPersonaReportGenerator>();
        var document = new ReportDocument(DateTimeOffset.UtcNow, "Period", "Summary", Array.Empty<ReportCurrentDevelopment>(), Array.Empty<ReportTrend>(), Array.Empty<TrendCorrelation>(), Array.Empty<PersonaReportSection>(), Array.Empty<SourceReference>());
        generator.GenerateAsync(Arg.Any<IReadOnlyCollection<IntelligenceItem>>(), Arg.Any<IReadOnlyCollection<TrendCorrelation>>(), Arg.Any<IReadOnlyCollection<TrendEvidence>>(), Arg.Any<CancellationToken>()).Returns(document);
        var tool = new GeneratePersonaReportTool(generator);

        var result = await tool.ExecuteAsync(new GeneratePersonaReportInput(Array.Empty<IntelligenceItem>(), Array.Empty<TrendCorrelation>(), Array.Empty<TrendEvidence>()), CancellationToken.None);

        Assert.Same(document, result);
        await generator.Received(1).GenerateAsync(Arg.Any<IReadOnlyCollection<IntelligenceItem>>(), Arg.Any<IReadOnlyCollection<TrendCorrelation>>(), Arg.Any<IReadOnlyCollection<TrendEvidence>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RenderReportTool_DelegatesToRenderer()
    {
        var renderer = new MarkdownReportRenderer();
        var document = new ReportDocument(DateTimeOffset.UtcNow, "Period", "Summary", Array.Empty<ReportCurrentDevelopment>(), Array.Empty<ReportTrend>(), Array.Empty<TrendCorrelation>(), Array.Empty<PersonaReportSection>(), Array.Empty<SourceReference>());
        var tool = new RenderReportTool(renderer);

        var result = tool.Execute(new RenderReportInput(document));

        Assert.Contains("AI Technology Intelligence Report", result);
    }

    [Fact]
    public async Task IngestSourcesTool_DelegatesToService()
    {
        var connectorFactory = Substitute.For<ISourceConnectorFactory>();
        var rawRepository = Substitute.For<Application.Persistence.IRawSourceRepository>();
        var ingestionService = new SourceIngestionService(
            connectorFactory,
            rawRepository,
            NullLogger<SourceIngestionService>.Instance);
        var source = new SourceDefinition(Guid.NewGuid(), "Source", "Vendor", SourceType.WebPage, SourceClass.CurrentOfficial, new Uri("https://example.com"), true);
        var ingestResult = new SourceIngestResult(0, 0, 0, Array.Empty<SourceIngestionFailure>());
        rawRepository.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var tool = new IngestSourcesTool(ingestionService);

        var result = await tool.ExecuteAsync(new IngestSourcesInput(new[] { source }), CancellationToken.None);

        Assert.Equal(ingestResult.FetchedCount, result.FetchedCount);
        Assert.Equal(ingestResult.InsertedCount, result.InsertedCount);
        Assert.Equal(ingestResult.DuplicateCount, result.DuplicateCount);
        await rawRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static IntelligenceItem CreateIntelligence()
    {
        return new IntelligenceItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Vendor",
            "Topic",
            "Category",
            "Framework",
            "Summary",
            Array.Empty<string>(),
            Array.Empty<string>(),
            "Unknown",
            SourceClass.CurrentOfficial,
            DateTimeOffset.UtcNow,
            new Uri("https://example.com"));
    }
}
