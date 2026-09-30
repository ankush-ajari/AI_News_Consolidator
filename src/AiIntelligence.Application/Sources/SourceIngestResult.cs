namespace AiIntelligence.Application.Sources;

public sealed record SourceIngestResult(
    int FetchedCount,
    int InsertedCount,
    int DuplicateCount,
    IReadOnlyCollection<SourceIngestionFailure> Failures);
