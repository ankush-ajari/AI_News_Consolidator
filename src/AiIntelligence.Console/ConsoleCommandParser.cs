namespace AiIntelligence.Console;

public static class ConsoleCommandParser
{
    public static bool HasFlag(string[] args, string flag)
    {
        return args.Any(argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));
    }

    public static int? GetIntOption(string[] args, string optionName)
    {
        var value = GetStringOption(args, optionName);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    public static string? GetStringOption(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    public static bool IsSupportedCommand(string command)
    {
        return string.Equals(command, "fetch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "ingest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "analyze", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "analyze-trends", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "reset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "report", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "generate-docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "test-llm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "run-workflow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "backfill-concepts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "backfill-trend-families", StringComparison.OrdinalIgnoreCase);
    }
}
