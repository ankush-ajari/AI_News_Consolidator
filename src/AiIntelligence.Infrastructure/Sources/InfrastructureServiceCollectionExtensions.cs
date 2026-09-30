using AiIntelligence.Application.Content;
using AiIntelligence.Application.Intelligence;
using AiIntelligence.Application.Persistence;
using AiIntelligence.Application.Reporting;
using AiIntelligence.Application.Sources;
using AiIntelligence.Infrastructure.Inspection;
using AiIntelligence.Infrastructure.Intelligence;
using AiIntelligence.Infrastructure.Maintenance;
using AiIntelligence.Infrastructure.Persistence;
using AiIntelligence.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiIntelligence.Infrastructure.Sources;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSourceIngestionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GitHubReleaseSourceOptions>(configuration.GetSection(GitHubReleaseSourceOptions.SectionName));
        services.Configure<LlmClientOptions>(configuration.GetSection(LlmClientOptions.SectionName));

        var connectionString = configuration.GetConnectionString("AiIntelligence") ?? "Data Source=ai-intelligence.db";
        services.AddDbContext<AiIntelligenceDbContext>(options => options.UseSqlite(connectionString));

        services.AddSingleton<IContentHashService, Sha256ContentHashService>();
        services.AddScoped<ISourceDefinitionRepository, SourceDefinitionRepository>();
        services.AddScoped<IRawSourceRepository, RawSourceRepository>();
        services.AddScoped<IIntelligenceRepository, IntelligenceRepository>();
        services.AddScoped<ITrendEvidenceRepository, TrendEvidenceRepository>();

        services.AddHttpClient(RssSourceConnector.HttpClientName);
        services.AddHttpClient(WebPageSourceConnector.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AiIntelligencePoc/1.0");
        });
        services.AddHttpClient(GitHubReleaseSourceConnector.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AiIntelligencePoc/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });
        services.AddHttpClient(HttpJsonLlmClient.HttpClientName);

        services.AddScoped<ILLMClient, HttpJsonLlmClient>();
        services.AddScoped<IIntelligenceExtractor, LlmIntelligenceExtractor>();
        services.AddScoped<ITrendEvidenceExtractor, LlmTrendEvidenceExtractor>();
        services.AddScoped<IntelligenceAnalysisService>();
        services.AddScoped<TrendAnalysisService>();
        services.AddScoped<InspectionService>();
        services.AddScoped<MaintenanceResetService>();
        services.AddScoped<ITrendCandidateSelector, TrendCandidateSelector>();
        services.AddScoped<ITrendCorrelationService, LlmTrendCorrelationService>();
        services.AddScoped<IPersonaReportGenerator, LlmPersonaReportGenerator>();
        services.AddScoped<MarkdownReportRenderer>();
        services.AddScoped<ReportOrchestrationService>();

        services.AddTransient<ISourceConnector, RssSourceConnector>();
        services.AddTransient<ISourceConnector, WebPageSourceConnector>();
        services.AddTransient<ISourceConnector, GitHubReleaseSourceConnector>();
        services.AddScoped<ISourceConnectorFactory, SourceConnectorFactory>();
        services.AddScoped<SourceIngestionService>();

        return services;
    }
}
