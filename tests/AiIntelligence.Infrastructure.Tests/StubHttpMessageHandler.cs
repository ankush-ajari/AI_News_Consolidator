using System.Net;

namespace AiIntelligence.Infrastructure.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        Requests.Add(request);
        return _handler(request, cancellationToken);
    }

    public static StubHttpMessageHandler WithStringResponse(string content, string mediaType = "application/json")
    {
        return new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
        }));
    }
}
