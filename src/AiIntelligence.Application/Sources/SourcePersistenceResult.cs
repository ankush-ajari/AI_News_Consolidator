namespace AiIntelligence.Application.Sources;

public sealed record SourcePersistenceResult(
    int InsertedCount,
    int DuplicateCount);
