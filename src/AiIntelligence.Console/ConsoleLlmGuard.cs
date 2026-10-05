using AiIntelligence.Infrastructure.Intelligence;

namespace AiIntelligence.Console;

public static class ConsoleLlmGuard
{
    public static bool IsApiKeyMissingForCommand(string command, bool shouldUseMock, LlmClientOptions options)
    {
        return !shouldUseMock
            && IsLlmRequiredCommand(command)
            && string.IsNullOrWhiteSpace(options.ApiKey);
    }

    public static bool IsLlmRequiredCommand(string command)
    {
        return string.Equals(command, "analyze", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "analyze-trends", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "run-workflow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "test-llm", StringComparison.OrdinalIgnoreCase);
    }
}
