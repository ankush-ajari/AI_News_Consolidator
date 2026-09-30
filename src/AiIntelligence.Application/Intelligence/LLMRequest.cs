namespace AiIntelligence.Application.Intelligence;

public sealed record LLMRequest(
    string SystemPrompt,
    string UserPrompt,
    string? JsonSchemaName,
    string? JsonSchema);
