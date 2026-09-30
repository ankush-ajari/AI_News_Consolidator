namespace AiIntelligence.Console;

public static class ConsoleLlmModeDecider
{
    public static bool ShouldUseMock(string command, bool mockFlag, bool hasApiKey)
    {
        if (mockFlag)
        {
            return true;
        }

        return string.Equals(command, "report", StringComparison.OrdinalIgnoreCase)
            && !hasApiKey;
    }
}
