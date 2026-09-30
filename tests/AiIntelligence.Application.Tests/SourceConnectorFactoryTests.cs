using AiIntelligence.Application.Sources;
using AiIntelligence.Domain.Enums;
using AiIntelligence.Domain.Models;
using NSubstitute;

namespace AiIntelligence.Application.Tests;

public sealed class SourceConnectorFactoryTests
{
    [Fact]
    public void GetConnector_ReturnsConnector_ForMatchingSourceType()
    {
        var rssConnector = Substitute.For<ISourceConnector>();
        rssConnector.SourceType.Returns(SourceType.Rss);

        var githubConnector = Substitute.For<ISourceConnector>();
        githubConnector.SourceType.Returns(SourceType.GitHubRelease);

        var factory = new SourceConnectorFactory(new[] { rssConnector, githubConnector });

        var connector = factory.GetConnector(SourceType.GitHubRelease);

        Assert.Same(githubConnector, connector);
    }

    [Fact]
    public void GetConnector_Throws_ForUnsupportedSourceType()
    {
        var rssConnector = Substitute.For<ISourceConnector>();
        rssConnector.SourceType.Returns(SourceType.Rss);
        var factory = new SourceConnectorFactory(new[] { rssConnector });

        Assert.Throws<NotSupportedException>(() => factory.GetConnector(SourceType.Api));
    }
}
