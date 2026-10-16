using System.Net;
using System.Text;

namespace TicketMonitor.Watchdog.Tests.TestHelpers;

/// <summary>Фейковый HTTP-обработчик для тестов <see cref="HttpClient"/>.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public List<CapturedRequest> Requests { get; } = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string content = "")
        : this(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        })
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));
        return Task.FromResult(_handler(request));
    }

    public sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string Body);
}