using AiIntelligence.Application.Content;

namespace AiIntelligence.Application.Tests;

public sealed class Sha256ContentHashServiceTests
{
    [Fact]
    public void ComputeHash_ReturnsDeterministicSha256Hash()
    {
        var service = new Sha256ContentHashService();

        var first = service.ComputeHash("hello world");
        var second = service.ComputeHash("hello world");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", first);
    }

    [Fact]
    public void ComputeHash_NormalizesLineEndingsAndOuterWhitespace()
    {
        var service = new Sha256ContentHashService();

        Assert.Equal(service.ComputeHash("value\n"), service.ComputeHash("  value\r\n"));
    }
}
