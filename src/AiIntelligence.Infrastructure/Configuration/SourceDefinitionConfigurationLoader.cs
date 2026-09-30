using System.Security.Cryptography;
using System.Text;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using Microsoft.Extensions.Configuration;

namespace AiIntelligence.Infrastructure.Configuration;

public static class SourceDefinitionConfigurationLoader
{
    public static IReadOnlyCollection<SourceDefinition> Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var sources = configuration.GetSection("Sources:Definitions")
            .GetChildren()
            .Select(CreateSourceDefinition)
            .ToArray();

        var duplicateIds = sources.GroupBy(source => source.Id).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate source definition id(s): {string.Join(", ", duplicateIds)}");
        }

        return sources;
    }

    private static SourceDefinition CreateSourceDefinition(IConfigurationSection section)
    {
        var name = GetRequiredValue(section, "name");
        var url = GetRequiredValue(section, "url");
        var sourceClass = Enum.Parse<SourceClass>(GetRequiredValue(section, "sourceClass"), ignoreCase: true);
        var idText = section["id"];
        var hasConfiguredId = Guid.TryParse(idText, out var configuredId) && configuredId != Guid.Empty;
        if (sourceClass == SourceClass.TrendResearch && !hasConfiguredId)
        {
            throw new InvalidOperationException($"TrendResearch source '{name}' must define a stable non-empty GUID id.");
        }

        var id = hasConfiguredId ? configuredId : CreateDeterministicId(name, url);

        return new SourceDefinition(
            id,
            name,
            GetRequiredValue(section, "vendor"),
            Enum.Parse<SourceType>(GetRequiredValue(section, "sourceType"), ignoreCase: true),
            sourceClass,
            new Uri(url),
            bool.TryParse(section["isEnabled"], out var isEnabled) && isEnabled,
            section.GetSection("includeTopicHints").GetChildren().Select(child => child.Value ?? string.Empty).ToArray());
    }

    private static string GetRequiredValue(IConfigurationSection section, string key)
    {
        return string.IsNullOrWhiteSpace(section[key])
            ? throw new InvalidOperationException($"Source configuration value '{key}' is required at '{section.Path}'.")
            : section[key]!;
    }

    private static Guid CreateDeterministicId(string name, string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{name}|{url}"));
        return new Guid(bytes[..16]);
    }
}
