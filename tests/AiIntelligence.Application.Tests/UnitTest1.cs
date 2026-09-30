namespace AiIntelligence.Application.Tests;

public sealed class ApplicationAssemblyTests
{
    [Fact]
    public void ApplicationAssembly_IsLoadable()
    {
        var assembly = typeof(ApplicationAssemblyTests).Assembly;

        Assert.Equal("AiIntelligence.Application.Tests", assembly.GetName().Name);
    }
}
