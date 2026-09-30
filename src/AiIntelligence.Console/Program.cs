using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Infrastructure.Configuration;
using AiIntelligence.Infrastructure.Inspection;
using AiIntelligence.Infrastructure.Intelligence;
using AiIntelligence.Infrastructure.Maintenance;
using AiIntelligence.Infrastructure.Persistence;
using AiIntelligence.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services.AddSourceIngestionInfrastructure(builder.Configuration);

if (args.Any(argument => string.Equals(argument, "--mock-llm", StringComparison.OrdinalIgnoreCase)))
{
    builder.Services.AddScoped<ILLMClient, MockLlmClient>();
    builder.Services.AddScoped<ITrendCorrelationService, MockTrendCorrelationService>();
    builder.Services.AddScoped<IPersonaReportGenerator, MockPersonaReportGenerator>();
}

using var host = builder.Build();

if (args.Length == 0 || !IsSupportedCommand(args[0]))
{
    Console.WriteLine("Usage: dotnet run --project src/AiIntelligence.Console -- <fetch|ingest|analyze|analyze-trends|inspect|reset|report|test-llm> [sources|raw|trends|stats|analysis|intelligence] [options]");
    return;
}

var command = args[0];
var verbose = args.Any(argument => string.Equals(argument, "--verbose", StringComparison.OrdinalIgnoreCase));
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FetchCommand");

if (string.Equals(command, "test-llm", StringComparison.OrdinalIgnoreCase))
{
    await using var testScope = host.Services.CreateAsyncScope();
    var llmClient = testScope.ServiceProvider.GetRequiredService<ILLMClient>();
    var response = await llmClient.CompleteAsync(
        new LLMRequest("Return exactly the requested text. Do not add punctuation.", "Return exactly: MODEL_OK", null, null),
        CancellationToken.None).ConfigureAwait(false);

    Console.WriteLine(response.Content.Trim());
    return;
}

var sourceDefinitions = SourceDefinitionConfigurationLoader.Load(builder.Configuration);
var sourceFilter = GetStringOption(args, "--source");
if (!string.IsNullOrWhiteSpace(sourceFilter)
    && (string.Equals(command, "fetch", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "ingest", StringComparison.OrdinalIgnoreCase)))
{
    sourceDefinitions = sourceDefinitions
        .Where(source => source.Name.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase))
        .ToArray();
}
foreach (var source in sourceDefinitions)
{
    logger.LogInformation(
        "Configured source loaded. Name: {SourceName}; SourceClass: {SourceClass}; SourceType: {SourceType}; IsEnabled: {IsEnabled}",
        source.Name,
        source.SourceClass,
        source.SourceType,
        source.IsEnabled);
}

if (!string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase))
{
    if (sourceDefinitions.Count == 0)
    {
        Console.WriteLine("No sources are configured. Check appsettings.Development.json or Sources__Definitions environment variables.");
        return;
    }

    Console.WriteLine($"Configured sources: {sourceDefinitions.Count}");
}

try
{
    await using var scope = host.Services.CreateAsyncScope();
    var ingestionService = scope.ServiceProvider.GetRequiredService<SourceIngestionService>();

    if (string.Equals(command, "ingest", StringComparison.OrdinalIgnoreCase))
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AiIntelligenceDbContext>();
        await dbContext.Database.MigrateAsync().ConfigureAwait(false);

        var ingestResult = await ingestionService.IngestAsync(sourceDefinitions, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Fetched count: {ingestResult.FetchedCount}");
        Console.WriteLine($"Inserted count: {ingestResult.InsertedCount}");
        Console.WriteLine($"Duplicate count: {ingestResult.DuplicateCount}");
        Console.WriteLine($"Failure count: {ingestResult.Failures.Count}");
        return;
    }

    if (string.Equals(command, "analyze", StringComparison.OrdinalIgnoreCase))
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AiIntelligenceDbContext>();
        await dbContext.Database.MigrateAsync().ConfigureAwait(false);

        var analysisService = scope.ServiceProvider.GetRequiredService<IntelligenceAnalysisService>();
        var analysisResult = await analysisService.AnalyzeUnprocessedAsync(
            CancellationToken.None,
            GetIntOption(args, "--limit"),
            includeDiagnostics: verbose).ConfigureAwait(false);
        Console.WriteLine($"Processed count: {analysisResult.ProcessedCount}");
        Console.WriteLine($"Persisted count: {analysisResult.PersistedCount}");
        Console.WriteLine($"Irrelevant count: {analysisResult.IrrelevantCount}");
        Console.WriteLine($"Skipped non-current official count: {analysisResult.SkippedNonCurrentOfficialCount}");
        Console.WriteLine($"Failed count: {analysisResult.FailedCount}");

        if (verbose)
        {
            Console.WriteLine();
            Console.WriteLine("Relevance criteria prompt:");
            Console.WriteLine(analysisResult.RelevanceCriteriaPrompt.Trim());
            Console.WriteLine();
            Console.WriteLine("Candidate prioritization diagnostics:");
            foreach (var candidate in analysisResult.CandidateDiagnostics)
            {
                Console.WriteLine($"Candidate: {candidate.SourceName} | {candidate.Title}");
                Console.WriteLine($"  AI relevance score: {candidate.Score}");
                Console.WriteLine($"  Matched positive signals: {(candidate.PositiveSignals.Count == 0 ? "None" : string.Join(", ", candidate.PositiveSignals))}");
                Console.WriteLine($"  Matched negative signals: {(candidate.NegativeSignals.Count == 0 ? "None" : string.Join(", ", candidate.NegativeSignals))}");
                Console.WriteLine($"  Final rank: {candidate.Rank}");
            }

            Console.WriteLine();
            Console.WriteLine("Selected for LLM analysis:");
            foreach (var selected in analysisResult.SelectedCandidates)
            {
                Console.WriteLine($"- {selected.SourceName} | {selected.Title} | Score: {selected.Score} | Rank: {selected.Rank}");
            }

            Console.WriteLine();
            Console.WriteLine("Processed CurrentOfficial diagnostics:");
            foreach (var diagnostic in analysisResult.Diagnostics)
            {
                Console.WriteLine($"RawSourceItemId: {diagnostic.RawSourceItemId}");
                Console.WriteLine($"Source name: {diagnostic.SourceName}");
                Console.WriteLine($"Title: {diagnostic.Title}");
                Console.WriteLine($"PublishedDate: {diagnostic.PublishedAt?.ToString("O") ?? "Unknown"}");
                Console.WriteLine($"RawContent length: {diagnostic.RawContentLength}");
                Console.WriteLine($"RawContent preview: {diagnostic.RawContentPreview}");
                Console.WriteLine($"Topic hints matched: {(diagnostic.TopicHintsMatched.Count == 0 ? "None" : string.Join(", ", diagnostic.TopicHintsMatched))}");
                Console.WriteLine("LLM classification:");
                Console.WriteLine($"  IsRelevant: {diagnostic.ExtractionResult.IsRelevantToAI}");
                Console.WriteLine($"  Relevance reason: {(diagnostic.ExtractionResult.EvidenceStatements.Count == 0 ? "Unknown" : string.Join("; ", diagnostic.ExtractionResult.EvidenceStatements))}");
                Console.WriteLine($"  Topic: {diagnostic.ExtractionResult.Topic}");
                Console.WriteLine($"  Category: {diagnostic.ExtractionResult.Category}");
                Console.WriteLine($"  Product/framework: {diagnostic.ExtractionResult.ProductOrFramework}");
                Console.WriteLine($"  Summary: {diagnostic.ExtractionResult.Summary}");
                Console.WriteLine($"  Confidence: {diagnostic.ExtractionResult.RelevanceScore:0.00}");
                Console.WriteLine();
            }
        }

        var staleItems = await dbContext.IntelligenceItems
            .AsNoTracking()
            .Where(item => item.SourceClass != SourceClass.CurrentOfficial)
            .ToArrayAsync()
            .ConfigureAwait(false);
        if (staleItems.Length > 0)
        {
            var rawLookup = await dbContext.RawSourceItems
                .AsNoTracking()
                .ToDictionaryAsync(item => item.Id, CancellationToken.None)
                .ConfigureAwait(false);
            var sourceLookup = await dbContext.SourceDefinitions
                .AsNoTracking()
                .ToDictionaryAsync(source => source.Id, CancellationToken.None)
                .ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine($"Stale intelligence items (non-current official): {staleItems.Length}");
            foreach (var item in staleItems)
            {
                rawLookup.TryGetValue(item.SourceItemId, out var rawItem);
                var sourceName = rawItem is not null && sourceLookup.TryGetValue(rawItem.SourceDefinitionId, out var source)
                    ? source.Name
                    : "n/a";
                var rawClass = rawItem?.SourceClass.ToString() ?? "n/a";
                var rawUrl = rawItem?.Url.ToString() ?? "n/a";

                Console.WriteLine($"- IntelligenceItemId: {item.Id} | SourceItemId: {item.SourceItemId} | SourceClass: {item.SourceClass} | SourceName: {sourceName} | SourceUrl: {item.SourceUrl}");
                if (verbose)
                {
                    Console.WriteLine($"  RawSourceClass: {rawClass} | RawUrl: {rawUrl}");
                }
            }
        }

        return;
    }

    if (string.Equals(command, "analyze-trends", StringComparison.OrdinalIgnoreCase))
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AiIntelligenceDbContext>();
        await dbContext.Database.MigrateAsync().ConfigureAwait(false);

        if (args.Any(argument => string.Equals(argument, "--mock-llm", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("WARNING: MOCK LLM MODE");
            Console.WriteLine("Trend evidence generated in this run is synthetic.");
            Console.WriteLine("Use it only to validate workflow/persistence behavior.");
        }

        var trendAnalysisService = scope.ServiceProvider.GetRequiredService<TrendAnalysisService>();
        var trendResult = await trendAnalysisService.AnalyzeTrendResearchAsync(CancellationToken.None, GetIntOption(args, "--limit")).ConfigureAwait(false);
        Console.WriteLine($"Processed count: {trendResult.ProcessedCount}");
        Console.WriteLine($"Persisted count: {trendResult.PersistedCount}");
        Console.WriteLine($"Skipped non-trend research count: {trendResult.SkippedNonTrendResearchCount}");
        Console.WriteLine($"Failed count: {trendResult.FailedCount}");
        return;
    }

    if (string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase))
    {
        var inspectionService = scope.ServiceProvider.GetRequiredService<InspectionService>();
        await RunInspectAsync(args, inspectionService, sourceDefinitions, CancellationToken.None).ConfigureAwait(false);
        return;
    }

    if (string.Equals(command, "report", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Any(argument => string.Equals(argument, "--mock-llm", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("WARNING: MOCK LLM MODE");
            Console.WriteLine("Correlation and persona content is synthetic and validates workflow behavior only.");
        }

        var reportService = scope.ServiceProvider.GetRequiredService<ReportOrchestrationService>();
        var (reportResult, markdown) = await reportService.GenerateAsync(GetIntOption(args, "--limit"), verbose, CancellationToken.None).ConfigureAwait(false);
        var outputDirectory = Path.Combine(Environment.CurrentDirectory, "output");
        Directory.CreateDirectory(outputDirectory);
        var outputFile = Path.Combine(outputDirectory, "ai-intelligence-report.md");
        await File.WriteAllTextAsync(outputFile, markdown, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine("Report generated successfully");
        Console.WriteLine($"IntelligenceItems processed: {reportResult.IntelligenceItemsProcessed}");
        Console.WriteLine($"Trend candidates considered: {reportResult.TrendCandidatesConsidered}");
        Console.WriteLine($"Correlations created: {reportResult.CorrelationsCreated}");
        Console.WriteLine($"InsufficientEvidence correlations: {reportResult.InsufficientEvidenceCorrelations}");
        Console.WriteLine($"Persona sections created: {reportResult.PersonaSectionsCreated}");
        Console.WriteLine($"Output file path: {outputFile}");
        Console.WriteLine($"LLM call count if available: {reportResult.LlmCallCount}");
        return;
    }

    if (string.Equals(command, "reset", StringComparison.OrdinalIgnoreCase))
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AiIntelligenceDbContext>();
        await dbContext.Database.MigrateAsync().ConfigureAwait(false);

        var resetService = scope.ServiceProvider.GetRequiredService<MaintenanceResetService>();
        await RunResetAsync(args, resetService, CancellationToken.None).ConfigureAwait(false);
        return;
    }

    var result = await ingestionService.FetchAsync(sourceDefinitions, CancellationToken.None).ConfigureAwait(false);

    Console.WriteLine($"Fetched items: {result.Items.Count}");
    Console.WriteLine($"Failures: {result.Failures.Count}");
    Console.WriteLine();

    foreach (var item in result.Items)
    {
        var source = sourceDefinitions.First(definition => definition.Id == item.SourceDefinitionId);

        Console.WriteLine($"Source: {source.Name}");
        Console.WriteLine($"Title: {item.Title}");
        Console.WriteLine($"PublishedDate: {item.PublishedAt?.ToString("O") ?? "n/a"}");
        Console.WriteLine($"Primary URL: {item.Url}");
        Console.WriteLine($"RawContent length: {item.RawContent.Length}");
        Console.WriteLine($"EnrichedContent length: {item.EnrichedContent.Length}");

        if (verbose)
        {
            Console.WriteLine("RawContent:");
            Console.WriteLine(item.RawContent);
            Console.WriteLine("EnrichedContent:");
            Console.WriteLine(item.EnrichedContent);
        }

        Console.WriteLine();
    }

    if (result.Failures.Count > 0)
    {
        Console.Error.WriteLine("Failures:");
        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"- {failure.Source.Name}: {failure.ErrorMessage}");
        }
    }
}
catch (OperationCanceledException exception)
{
    logger.LogWarning(exception, "Fetch command was cancelled.");
    throw;
}

static async Task RunInspectAsync(
    string[] args,
    InspectionService inspectionService,
    IReadOnlyCollection<AiIntelligence.Domain.Models.SourceDefinition> configuredSources,
    CancellationToken cancellationToken)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: inspect <sources|raw|trends|stats>");
        return;
    }

    var target = args[1];
    if (string.Equals(target, "sources", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var row in await inspectionService.GetSourcesAsync(configuredSources, cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine($"Source Name: {row.SourceName}");
            Console.WriteLine($"Vendor: {row.Vendor}");
            Console.WriteLine($"SourceType: {row.SourceType}");
            Console.WriteLine($"SourceClass: {row.SourceClass}");
            Console.WriteLine($"Enabled: {row.Enabled}");
            Console.WriteLine($"Raw record count: {row.RawRecordCount}");
            Console.WriteLine($"Latest PublishedAt: {row.LatestPublishedAt?.ToString("O") ?? "n/a"}");
            Console.WriteLine($"Latest FetchedAt: {row.LatestFetchedAt?.ToString("O") ?? "n/a"}");
            Console.WriteLine();
        }

        return;
    }

    if (string.Equals(target, "raw", StringComparison.OrdinalIgnoreCase))
    {
        var sourceClass = GetStringOption(args, "--class") is { } classText
            && Enum.TryParse<SourceClass>(classText, ignoreCase: true, out var parsedClass)
                ? parsedClass
                : (SourceClass?)null;
        var rows = await inspectionService.GetRawAsync(new RawInspectionFilter(
            GetIntOption(args, "--limit") ?? 20,
            GetStringOption(args, "--source"),
            sourceClass,
            GetIntOption(args, "--min-length")), cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            Console.WriteLine($"Id: {row.Id}");
            Console.WriteLine($"Source Name: {row.SourceName}");
            Console.WriteLine($"SourceClass: {row.SourceClass}");
            Console.WriteLine($"Title: {row.Title}");
            Console.WriteLine($"PublishedAt: {row.PublishedAt?.ToString("O") ?? "n/a"}");
            Console.WriteLine($"FetchedAt: {row.FetchedAt:O}");
            Console.WriteLine($"URL: {row.Url}");
            Console.WriteLine($"RawContent length: {row.RawContentLength}");
            Console.WriteLine($"EnrichedContent length: {row.EnrichedContentLength}");
            Console.WriteLine($"Preview: {row.Preview}");
            Console.WriteLine();
        }

        return;
    }

    if (string.Equals(target, "trends", StringComparison.OrdinalIgnoreCase))
    {
        var rows = await inspectionService.GetTrendsAsync(new TrendInspectionFilter(
            GetIntOption(args, "--limit") ?? 20,
            GetStringOption(args, "--source"),
            GetStringOption(args, "--topic")), cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            Console.WriteLine($"Id: {row.Id}");
            Console.WriteLine($"Source Name: {row.SourceName}");
            Console.WriteLine($"Topic: {row.Topic}");
            Console.WriteLine($"Period: {row.Period}");
            Console.WriteLine($"Finding: {row.Finding}");
            Console.WriteLine($"EvidenceSummary: {row.EvidenceSummary}");
            Console.WriteLine($"Confidence: {row.Confidence}");
            Console.WriteLine($"Source URL: {row.SourceUrl}");
            Console.WriteLine();
        }

        return;
    }

    if (string.Equals(target, "stats", StringComparison.OrdinalIgnoreCase))
    {
        var stats = await inspectionService.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"SourceDefinitions count: {stats.SourceDefinitionsCount}");
        Console.WriteLine($"RawSourceItems count: {stats.RawSourceItemsCount}");
        Console.WriteLine($"CurrentOfficial count: {stats.CurrentOfficialCount}");
        Console.WriteLine($"TrendResearch count: {stats.TrendResearchCount}");
        Console.WriteLine($"ResearchDiscovery count: {stats.ResearchDiscoveryCount}");
        Console.WriteLine($"IntelligenceItems count: {stats.IntelligenceItemsCount}");
        Console.WriteLine($"TrendEvidence count: {stats.TrendEvidenceCount}");
        Console.WriteLine();
        Console.WriteLine("RawSourceItems by source:");
        foreach (var row in stats.RawItemsBySource)
        {
            Console.WriteLine($"{row.SourceName} | {row.SourceClass} | {row.Count}");
        }
        Console.WriteLine();
        Console.WriteLine("TrendEvidence by source:");
        foreach (var row in stats.TrendEvidenceBySource)
        {
            Console.WriteLine($"{row.SourceName} | {row.SourceClass} | {row.Count}");
        }
        Console.WriteLine();
        Console.WriteLine($"Minimum RawContent length: {stats.ContentQuality.MinimumRawContentLength}");
        Console.WriteLine($"Average RawContent length: {stats.ContentQuality.AverageRawContentLength:F2}");
        Console.WriteLine($"Maximum RawContent length: {stats.ContentQuality.MaximumRawContentLength}");
        Console.WriteLine($"Count where RawContent < 300 characters: {stats.ContentQuality.CountRawContentLessThan300}");
        Console.WriteLine($"Count where RawContent is null or empty: {stats.ContentQuality.CountRawContentNullOrEmpty}");
        return;
    }

    Console.WriteLine("Unknown inspect target. Use sources, raw, trends, or stats.");
}

static async Task RunResetAsync(string[] args, MaintenanceResetService resetService, CancellationToken cancellationToken)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: reset <trends|intelligence|analysis|invalid-intelligence> --confirm");
        return;
    }

    var confirmed = args.Any(argument => string.Equals(argument, "--confirm", StringComparison.OrdinalIgnoreCase));
    var target = args[1];

    if (string.Equals(target, "trends", StringComparison.OrdinalIgnoreCase))
    {
        var result = await resetService.ResetTrendsAsync(confirmed, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Deleted TrendEvidence rows: {result.DeletedTrendEvidenceCount}");
        return;
    }

    if (string.Equals(target, "intelligence", StringComparison.OrdinalIgnoreCase))
    {
        var result = await resetService.ResetIntelligenceAsync(confirmed, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Deleted IntelligenceItems rows: {result.DeletedIntelligenceItemsCount}");
        return;
    }

    if (string.Equals(target, "analysis", StringComparison.OrdinalIgnoreCase))
    {
        var result = await resetService.ResetAnalysisAsync(confirmed, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Deleted IntelligenceItems rows: {result.DeletedIntelligenceItemsCount}");
        Console.WriteLine($"Deleted TrendEvidence rows: {result.DeletedTrendEvidenceCount}");
        return;
    }

    if (string.Equals(target, "invalid-intelligence", StringComparison.OrdinalIgnoreCase))
    {
        var result = await resetService.ResetInvalidIntelligenceAsync(confirmed, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Invalid IntelligenceItems found: {result.InvalidIntelligenceCount}");
        Console.WriteLine($"Deleted: {result.DeletedCount}");
        Console.WriteLine($"Remaining valid IntelligenceItems: {result.RemainingValidCount}");

        foreach (var item in result.DeletedItems)
        {
            Console.WriteLine($"- IntelligenceItemId: {item.IntelligenceItemId} | SourceName: {item.SourceName} | SourceClass: {item.SourceClass} | SourceUrl: {item.SourceUrl}");
        }

        return;
    }

    Console.WriteLine("Unknown reset target. Use trends, intelligence, analysis, or invalid-intelligence.");
}

static int? GetIntOption(string[] args, string optionName)
{
    var value = GetStringOption(args, optionName);
    return int.TryParse(value, out var parsed) ? parsed : null;
}

static string? GetStringOption(string[] args, string optionName)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static bool IsSupportedCommand(string command)
{
    return string.Equals(command, "fetch", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "ingest", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "analyze", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "analyze-trends", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "reset", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "report", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "test-llm", StringComparison.OrdinalIgnoreCase);
}

