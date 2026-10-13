# AI Technology Intelligence POC - Semantic Rules, Signals and LLM Points

This document lists deterministic keyword/signal checks, signal lists, and the places in the code where the LLM is used. All items are grounded in source files and unit tests.

## 1. Where the code invokes an LLM

The code calls `ILLMClient` in these concrete places (files/classes):

- `LlmPersonaReportGenerator` (`src/AiIntelligence.Application/Reporting/LlmPersonaReportGenerator.cs`) — generates persona JSON per role using a schema and strict parsing.
- `LlmTrendCorrelationService` (`src/AiIntelligence.Application/Reporting/LlmTrendCorrelationService.cs`) — maps current developments to trend relationships using LLM completion and validates JSON output.
- `LlmTrendEvidenceExtractor` / `LlmTrendEvidenceExtractor`-related classes (`src/AiIntelligence.Application/Intelligence`) — extract trend evidence and fields from long-form sources.
- `LlmIntelligenceExtractor` (`src/AiIntelligence.Application/Intelligence/LlmIntelligenceExtractor.cs`) — used during intelligence analysis to extract structured fields from raw source content.
- `HttpJsonLlmClient` (`src/AiIntelligence.Infrastructure/Intelligence/HttpJsonLlmClient.cs`) — concrete client that sends requests to a configured `Foundry` endpoint and parses responses.

Unit tests mock `ILLMClient` to validate parsing and fallback behavior (see `tests/AiIntelligence.Application.Tests/ReportingTests.cs` and other test files).


## 2. Deterministic signal lists and keyword checks (explicit in code)

Several deterministic functions and unit tests enumerate keyword lists used to score or gate relevance. These lists are implemented in `LlmPersonaReportGenerator.DetermineRelevance(...)` and related helpers:

- Developer signals (examples from code):
  - "programming language", "language", "developer platform", "framework", "sdk", "api", "coding tool", "tooling", "architecture", "integration", "ai-assisted", "copilot", "implementation", "developer experience", "dx", "platform"
- QA signals:
  - "test", "testability", "reliability", "evaluation", "automation", "observability", "quality", "risk", "regression", "safety", "lifecycle"
- Business Analyst signals:
  - "workflow", "requirement", "capability", "process", "automation", "adoption", "business", "stakeholder", "user"
- Project Manager signals:
  - "delivery", "productivity", "skills", "adoption", "dependency", "risk", "governance", "planning", "schedule", "readiness"
- Sales signals:
  - "enterprise", "customer", "positioning", "market", "use case", "adoption", "platform", "product", "demand", "signal"

These lists are used by deterministic scoring functions in `LlmPersonaReportGenerator` to produce a persona relevance assessment.


## 3. Candidate selection heuristics (deterministic)

`TrendCandidateSelector` (`src/AiIntelligence.Application/Reporting/TrendCandidateSelector.cs`) implements deterministic scoring to pick top-K candidates for each intelligence item. Key behaviors implemented:

- Streams candidates via repository projection APIs (e.g., `StreamCandidatesForCorrelationAsync`) to avoid loading entire tables.
- Scores candidates by multiple components (topic match, category match, product score, concept match counts, period specificity, confidence, family compatibility, independent source score).
- Uses SortedSet-like bounded selection (top-K) to keep only the highest scoring candidates.

The repository-level projection types include `TrendEvidenceCandidate` (`src/AiIntelligence.Application/Persistence/TrendEvidenceCandidate.cs`). Unit tests in `tests/AiIntelligence.Application.Tests/ReportingTests.cs` validate selection priorities (e.g., preferring independent sources when tie-breaking, respecting max candidate count, weighting families).


## 4. Deterministic deduplication

`TrendEvidenceDuplicateDetector` (`src/AiIntelligence.Application/Intelligence/TrendEvidenceDuplicateDetector.cs`) implements deterministic duplicate detection and grouping logic used by both trend analysis and persona/report composition.


## 5. LLM schema and guardrails (explicit)

- `LlmPersonaReportGenerator` constructs a persona section JSON schema string (`PersonaSectionJsonSchema`) and requests the LLM to return exactly one JSON object conforming to this schema. The generator then extracts and strictly parses the JSON; if parsing fails it falls back to deterministic persona skeletons with an explicit `RelevanceDetail` marking fallback.
- `LlmTrendCorrelationService` accepts LLM JSON output and validates expected fields. Malformed output causes the service to throw or to use deterministic fallbacks depending on the calling context (see unit tests `ReportingTests.CorrelationService_RejectsMalformedLlmOutput` and `CorrelationService_NoCandidates_ReturnsInsufficientEvidence_WithoutLlmCall`).


## 6. Sanitization and XML safety

- `OpenXmlReportDocumentGenerator` sanitizes all text written into OpenXML `Text` nodes using `XmlSafeTextSanitizer.Sanitize(...)` (`src/AiIntelligence.Application/Reporting/XmlSafeTextSanitizer.cs`). The sanitizer removes characters not allowed in XML 1.0 and returns the removed code points for logging.
- Tests ensure the sanitizer preserves valid XML characters and removes invalid control characters (see `tests/AiIntelligence.Application.Tests/ReportDocumentGeneratorTests.cs`).


## 7. Tests that assert signal behavior

- See `tests/AiIntelligence.Application.Tests/ReportingTests.cs` for tests that assert persona relevance gating, correlation mapping, candidate selection heuristics, and Markdown renderer output containing citations.


## 8. Not determinable from code alone

- Any heuristic thresholds tuned via external configuration at runtime (not present in checked-in configuration) — unless explicitly represented in code, the exact numeric thresholds are as coded.
- LLM prompt engineering beyond what's visible in source code (prompts are present in code but final LLM responses are runtime-dependent).


End of semantic rules and keywords document.
