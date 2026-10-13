# AI Technology Intelligence POC — Documentation Index

This docs/ folder contains concise, implementation-grounded technical documentation for the AI Technology Intelligence Proof-of-Concept currently implemented in this repository.

Files:

- 01-architecture.md — high-level architecture, workflow and data-flow diagrams grounded in the repository code.
- 02-cli-commands.md — CLI command reference based on `src/AiIntelligence.Console/Program.cs` and `src/AiIntelligence.Console/ConsoleCommandParser.cs`.
- 03-input-sources.md — how input sources are configured and the file locations used by the code.
- 04-semantic-rules-and-keywords.md — deterministic keyword/signal lists and places where the LLM is invoked; grounded in source files and unit tests.
- 05-code-statistics.md — concise repository and project-level statistics (projects, key modules, test locations, runtime targets).

Notes / constraints
- This documentation describes only what is implemented now in the repository — it does not describe planned features or inferred behavior outside the codebase.
- Statements are referenced to source files and tests where relevant.

For a fast start, read `01-architecture.md` then `02-cli-commands.md`.
