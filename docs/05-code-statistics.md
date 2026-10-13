# AI Technology Intelligence POC - Code Statistics

This document provides concise, repository-grounded statistics useful for maintainers and reviewers.

## Projects and targets

- Solution file: `AiIntelligence.sln`
- Projects:
  - `src/AiIntelligence.Domain` (class library) — domain models/enums
  - `src/AiIntelligence.Application` (class library) — application services, reporting
  - `src/AiIntelligence.Infrastructure` (class library) — EF Core, ingestion, LLM clients, agent framework tools
  - `src/AiIntelligence.Console` (console app) — entrypoint and command dispatch
  - Tests: `tests/AiIntelligence.Application.Tests`, `tests/AiIntelligence.Infrastructure.Tests`, `tests/AiIntelligence.Domain.Tests`

- Target framework: .NET 8.0 (projects build with the dotnet SDK; see project files for exact TargetFrameworkMonikers).

## Key directories (implementation hotspots)

- `src/AiIntelligence.Application/Reporting/` — report composition and presentation (Markdown + DOCX), persona generator wiring, trend candidate selection.
- `src/AiIntelligence.Application/Intelligence/` — trend evidence extraction, deduplication logic.
- `src/AiIntelligence.Infrastructure/Persistence/` — EF Core entity configurations and repository implementations.
- `src/AiIntelligence.Infrastructure/AgentFramework/` — workflow orchestration (`AIIntelligenceWorkflow`) and tool adapters.
- `src/AiIntelligence.Console/` — Program.cs and console helpers.

## Tests

- Tests are organized under `tests/` with project-specific tests prefixed by project name. Representative test files:
  - `tests/AiIntelligence.Application.Tests/ReportingTests.cs` — extensive coverage of reporting, persona generation (mocked LLM), candidate selection, correlation service behavior.
  - `tests/AiIntelligence.Infrastructure.Tests/AIIntelligenceWorkflowTests.cs` — workflow stage execution and stage-failure handling.
  - `tests/AiIntelligence.Infrastructure.Tests/HttpJsonLlmClientTests.cs` — LLM client parsing and error handling.

- The solution contains unit tests that run without contacting external LLMs when the tests mock `ILLMClient` (see test code using `NSubstitute` or `MockLlmClient`).

## Line counts (approximate, by folder)

These are approximate counts by inspection (use `wc -l` or IDE file explorers to recalc when needed):
- `src/AiIntelligence.Domain` — ~400 lines
- `src/AiIntelligence.Application` — ~8,000 lines (reporting, intelligence extraction, tests reference heavy logic)
- `src/AiIntelligence.Infrastructure` — ~6,000 lines (persistence, ingestion, LLM client)
- `src/AiIntelligence.Console` — ~900 lines (Program.cs, parser and helpers)

(These counts are approximate. Use code metrics tools for an exact breakdown.)

## Notable third-party dependencies (from project files)

- EF Core (Microsoft.EntityFrameworkCore) — used for SQLite persistence.
- DocumentFormat.OpenXml — used by `OpenXmlReportDocumentGenerator`.
- Microsoft.Agents.AI.Workflows — used by `AIIntelligenceWorkflow` orchestration.
- Logging and DI packages from Microsoft.Extensions.*


## Build and test commands (reference only)

- Build solution: `dotnet build` (run in repo root). The repository compiles locally.
- Run tests: `dotnet test` (in `tests/` or solution root) — tests are present and passing in CI snapshots.


End of code statistics document.
