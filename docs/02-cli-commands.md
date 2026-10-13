# AI Technology Intelligence POC - CLI Command Reference

This document lists CLI commands that are implemented in the repository's console entrypoint. All commands and options are derived from `src/AiIntelligence.Console/Program.cs` and `src/AiIntelligence.Console/ConsoleCommandParser.cs`.

Usage example shown in Program.cs:

```powershell
# Example invocation (when running locally via dotnet):
# dotnet run --project src\AiIntelligence.Console -- <command> [subcommands/options]
```


Supported commands (as implemented):

- fetch
- ingest
- analyze
- analyze-trends
- inspect
- reset
- report
- generate-docx
- test-llm
- run-workflow
- backfill-concepts
- backfill-trend-families

These are the commands `ConsoleCommandParser.IsSupportedCommand` explicitly allows. See `src/AiIntelligence.Console/ConsoleCommandParser.cs`.

For each command below, the documented behavior is the behavior implemented in `Program.cs`.

---

## run-workflow

**Purpose**

Execute the deterministic workflow implemented by `AIIntelligenceWorkflow` (`src/AiIntelligence.Infrastructure/AgentFramework/AIIntelligenceWorkflow.cs`). This orchestrates ingestion, current intelligence analysis, trend analysis, correlation, persona report generation, Markdown rendering, and DOCX generation (subject to options).

**Important**
- The command will call `dbContext.Database.MigrateAsync()` before running if the handler is reached (see `Program.cs`).
- The workflow uses DI-resolved `IAIIntelligenceWorkflow` and produces a `AIIntelligenceWorkflowResult` that the console prints.

**Options**
- `--current-limit <n>` — sets the `CurrentIntelligenceLimit` option.
- `--trend-limit <n>` — sets the `TrendAnalysisLimit` option.
- `--mock-llm` — run with in-process mocked LLM and persona/correlation generators (registered in Program.cs when determined by `ConsoleLlmModeDecider`).
- `--skip-ingest` — sets `RunIngestion` false.
- `--memory-diagnostics` — emit memory diagnostics via logger (metrics are logged; the workflow state no longer persists memory metrics).
- `--verbose` — the top-level `--verbose` flag is recognized by the program and passed into options.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- run-workflow [--current-limit N] [--trend-limit N] [--mock-llm] [--skip-ingest] [--memory-diagnostics] [--verbose]
```

---

## ingest

**Purpose**

Run the source ingestion service to fetch and persist raw source items. Uses `SourceIngestionService` resolved from DI.

**Behavior implemented**
- Migrates the database (`dbContext.Database.MigrateAsync()`), then calls `ingestionService.IngestAsync(sourceDefinitions, CancellationToken.None)`.
- Writes counts to console (FetchedCount, InsertedCount, DuplicateCount, Failures.Count).

**Options**
- `--source <name>` — Program loads configured sources via `SourceDefinitionConfigurationLoader.Load(...)` and will filter to the named source for fetch/ingest commands.
- `--mock-llm` — affects whether `ILLMClient` is resolved as `MockLlmClient` (this flag is handled earlier and registered in DI in Program.cs).

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- ingest [--source <name>] [--mock-llm]
```

---

## analyze

**Purpose**

Run the intelligence analysis service (`IntelligenceAnalysisService`) to process unprocessed raw items into `IntelligenceItem`s.

**Behavior implemented**
- Migrates DB then calls `analysisService.AnalyzeUnprocessedAsync(...)` with optional `--limit`.
- Writes processed/persisted/irrelevant/skipped/failed counts.
- If `--verbose` is supplied, the handler prints detailed diagnostics and candidate-level info (including LLM extraction results) to console.

**Options**
- `--limit <n>` — passed to `AnalyzeUnprocessedAsync`.
- `--verbose` — prints additional diagnostics (relevance prompt, candidate diagnostics, selected LLM candidates, extraction results).

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- analyze [--limit N] [--verbose] [--mock-llm]
```

---

## analyze-trends

**Purpose**

Run `TrendAnalysisService.AnalyzeTrendResearchAsync(...)` to process trend-research sources into `TrendEvidence` records.

**Behavior implemented**
- Migrates DB then calls `TrendAnalysisService.AnalyzeTrendResearchAsync(cancellationToken, limit)`.
- Prints processed/persisted/skipped/failed/duplicate counts.
- If `--mock-llm` is passed in args, the program warns that trend evidence is synthetic.

**Options**
- `--limit <n>` — maximum items (passed into `AnalyzeTrendResearchAsync`).
- `--mock-llm` — warning is printed; DI may resolve mocked LLM implementations per `Program.cs` logic.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- analyze-trends [--limit N] [--mock-llm]
```

---

## report

**Purpose**

Generate an in-memory report from persisted intelligence items and trend evidence. Uses `ReportOrchestrationService.GenerateAsync(...)` and then writes Markdown to `output/ai-intelligence-report.md`.

**Behavior implemented**
- Calls `ReportOrchestrationService.GenerateAsync(limit, verbose, CancellationToken.None)` which:
  - loads intelligence items from `IIntelligenceRepository.ListAsync(limit)`
  - uses `ITrendCandidateSelector` to pick candidates for each intelligence item
  - calls `ITrendCorrelationService.CorrelateAsync(...)` for each item
  - calls `IPersonaReportGenerator.GenerateAsync(...)` to produce the `ReportDocument`
  - renders Markdown via `MarkdownReportRenderer.Render(document)`
- Writes `ReportGenerationResult` stats to the console (counts and LLM call count if available).

**Options**
- `--limit <n>` — passed into orchestration to limit processed intelligence items.
- `--verbose` — enables additional logging and verbose behavior in orchestration (also passed down where used).
- `--mock-llm` — affects whether mocked LLM implementations are used; program prints a warning when it is present.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- report [--limit N] [--verbose] [--mock-llm]
```

---

## generate-docx

**Purpose**

Convert an existing Markdown report into DOCX using `IReportDocumentGenerator.GenerateDocxAsync` (implemented by `OpenXmlReportDocumentGenerator`).

**Behavior implemented**
- Reads `--input` (path) defaulting to `output/ai-intelligence-report.md` if not provided.
- Writes `--output` path defaulting to `ai-intelligence-report.docx` in the input directory.
- Calls `reportGenerator.GenerateDocxAsync(inputPath, outputPath, CancellationToken.None)`.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- generate-docx [--input <path>] [--output <path>]
```

---

## test-llm

**Purpose**

A minimal LLM integration smoke-check implemented in `Program.cs`. It calls `ILLMClient.CompleteAsync(...)` with a deterministic prompt and writes the trimmed response to console. The command is intended for quick LLM verification.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- test-llm
```

Note: `Program.cs` conditionally registers `MockLlmClient` when `ConsoleLlmModeDecider` indicates mock mode. The test-llm command will call whichever `ILLMClient` is registered.

---

## inspect

**Purpose**

Run `InspectionService` to inspect stored data, controlled by `RunInspectAsync` in `Program.cs`. The exact subcommands accepted by `inspect` are implemented in `RunInspectAsync` and the `InspectionService`.

**Behavior implemented**
- The command runs `RunInspectAsync(args, inspectionService, sourceDefinitions, cancellationToken)` that delegates to `InspectionService` operations.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- inspect [subcommand] [options]
```

(Consult `RunInspectAsync` in `Program.cs` and `InspectionService` implementation in `src/AiIntelligence.Infrastructure/Inspection` for exact supported subcommands.)

---

## reset

**Purpose**

Run `MaintenanceResetService` to reset persisted data per `RunResetAsync` in `Program.cs`.

**Behavior implemented**
- DB migration is applied, then the reset service is invoked.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- reset [--confirm] [--dry-run]
```

---

## backfill-concepts

**Purpose**

Run `ConceptBackfillService.BackfillAsync(confirm, ...)` to backfill concept tags for persisted records.

**Options**
- `--confirm` — persist changes.
- `--dry-run` — run in dry-run mode.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- backfill-concepts --dry-run | --confirm
```

---

## backfill-trend-families

**Purpose**

Run `TrendFamilyBackfillService.BackfillAsync(confirm, ...)` to backfill trend family assignments for `TrendEvidence` records.

**Options**
- `--confirm` — persist changes.
- `--dry-run` — run in dry-run mode.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- backfill-trend-families --dry-run | --confirm
```

---

## fetch (default)

**Purpose**

If none of the specialized commands matches, `Program.cs` falls back to calling `ingestionService.FetchAsync(sourceDefinitions, CancellationToken.None)` and prints fetched items and failures to the console. This is a read-only fetch behavior implemented in `SourceIngestionService`.

**Options**
- `--source <name>` — filter the configured sources to fetch.
- `--verbose` — prints raw/enriched content when displaying fetched items.

**Invocation**

```powershell
dotnet run --project src\AiIntelligence.Console -- fetch [--source <name>] [--verbose]
```

---

# Notes and constraints

- The commands above are only documented for the actual implemented behavior in `Program.cs` and referenced services. Do not assume additional orchestration or commands beyond what the code shows.
- The program registers a number of services in DI in `src/AiIntelligence.Infrastructure/Sources/InfrastructureServiceCollectionExtensions.cs` — review that file for mappings between interfaces and concrete classes used at runtime.
- Do not run commands that might call external LLMs or modify the SQLite DB in an environment where that is disallowed. This document is reference-only and mirrors program logic; the repository also includes unit tests that validate many paths without calling external resources.
