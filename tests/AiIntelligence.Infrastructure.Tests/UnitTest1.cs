namespace AiIntelligence.Infrastructure.Tests;

public sealed class InfrastructureAssemblyTests
{
    [Fact]
    public void InfrastructureAssembly_IsLoadable()
    {
        var assembly = typeof(InfrastructureAssemblyTests).Assembly;

        Assert.Equal("AiIntelligence.Infrastructure.Tests", assembly.GetName().Name);
    }
}
