using AiIntelligence.Application.Reporting;
using AiIntelligence.Console.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace AiIntelligence.Console.Tests;

public class CopyReportCommandTests
{
    [Fact]
    public async Task CopyReport_InvokesPublisher_WhenEnabled()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReportPublishing:Enabled"] = "true",
            ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:"
        }).Build();

        services.AddSingleton<IConfiguration>(config);

        var publisher = Substitute.For<AiIntelligence.Application.Reporting.IReportFilePublisher>();
        publisher.PublishDocxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReportPublishResult(true, "src","dest", null)));

        services.AddSingleton(publisher);

        var provider = services.BuildServiceProvider();

        var result = await CopyReportCommand.ExecuteAsync(provider, null, CancellationToken.None);

        Assert.True(result.Success);
        await publisher.Received(1).PublishDocxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CopyReport_Skips_WhenDisabled()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReportPublishing:Enabled"] = "false",
            ["ConnectionStrings:AiIntelligence"] = "Data Source=:memory:"
        }).Build();

        services.AddSingleton<IConfiguration>(config);

        var publisher = Substitute.For<AiIntelligence.Application.Reporting.IReportFilePublisher>();
        publisher.PublishDocxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReportPublishResult(false, "", "", null)));

        services.AddSingleton(publisher);

        var provider = services.BuildServiceProvider();

        var result = await CopyReportCommand.ExecuteAsync(provider, null, CancellationToken.None);

        Assert.False(result.Success);
    }
}
