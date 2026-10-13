# AI Technology Intelligence POC - Input Sources

This document describes how input sources are configured and used by the implementation in this repository.

All configuration and loading behavior referenced here are implemented in these files:

- `src/AiIntelligence.Console/appsettings.Development.json` (example development configuration)
- `src/AiIntelligence.Infrastructure/Configuration/SourceDefinitionConfigurationLoader.cs` (loader and deterministic id creation)
- `src/AiIntelligence.Infrastructure/Sources` (ingestion infrastructure and DI registration)


## How sources are configured

Source definitions are read from the IConfiguration section `Sources:Definitions` and bound via `SourceDefinitionConfigurationLoader.Load(IConfiguration)` (`src/AiIntelligence.Infrastructure/Configuration/SourceDefinitionConfigurationLoader.cs`).

Each source definition must include the following keys (the loader enforces them and throws on missing required values):

- `name` (string) — human friendly name
- `vendor` (string)
- `sourceType` (enumerated as `SourceType`) — see code for allowed enum values
- `sourceClass` (enumerated as `SourceClass`) — allowed values include `CurrentOfficial`, `TrendResearch`, and `ResearchDiscovery` (see `src/AiIntelligence.Domain/Enums/SourceClass.cs`).
- `url` (string) — base URL for the source
- `id` (optional) — GUID; if not provided a deterministic GUID is created for the source using a SHA-256 hash of `name|url` (first 16 bytes). **Exception:** when `sourceClass` is `TrendResearch` an explicit non-empty GUID `id` is required (the loader throws if absent).
- `isEnabled` (boolean) — optional (default false if missing)
- `includeTopicHints` (array of strings) — optional topic hints used by ingestion/analysis.

A sample development configuration is present at `src/AiIntelligence.Console/appsettings.Development.json` and includes multiple `CurrentOfficial` and `TrendResearch` entries. The connection string for the SQLite DB is also included in this file.


## Where the source definitions are loaded and used

- `Program.cs` loads source definitions early for commands that require them: `fetch`, `ingest`, `analyze-trends`, `run-workflow`, `backfill-concepts`, `backfill-trend-families`.
- `SourceIngestionService` and DI registration: `src/AiIntelligence.Infrastructure/Sources/InfrastructureServiceCollectionExtensions.cs` wires ingestion services and repositories and configures `LlmClientOptions` from `Foundry` configuration.


## Source classes and their roles (as implemented)

- CurrentOfficial: these are official vendor announcements and product pages. They flow into `SourceIngestionService` and `IntelligenceAnalysisService` to generate `IntelligenceItem` records.
- TrendResearch: long-form or research-oriented sources (e.g., reports) used by `TrendAnalysisService` to produce `TrendEvidence` records.
- ResearchDiscovery: used for discovery feeds (e.g., arXiv) and marked in configuration; ingestion/analysis code treats them as configured sources but their role depends on specific ingestion service implementations.


## Ingestion boundaries and limitations

- The ingestion implementation (`SourceIngestionService`) is in the infrastructure project; it fetches content and persists `RawSourceItem` records in the database (see `AiIntelligenceDbContext.RawSourceItems`). The exact fetch adapters exist under `src/AiIntelligence.Infrastructure/Sources`.
- The configuration loader enforces a stable ID for `TrendResearch` sources to prevent accidental duplication when grouping/suppression logic relies on source ids.


## Not determinable from repository

- The exact HTTP fetch behavior (timeouts, retries) depends on the specific source adapter implementation and HTTP client configuration inside `src/AiIntelligence.Infrastructure/Sources` — inspect those files for concrete behavior.
- Any external credentials or network connectivity settings are environment-specific and not contained in the repository (e.g., API keys for private feeds).


End of input sources document.
