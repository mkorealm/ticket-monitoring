using System.Net;
using System.Text;

namespace TicketMonitor.Redmine.Tests.TestHelpers;

/// <summary>
/// Фейковый HTTP-обработчик для тестов <see cref="HttpClient"/>.
/// Сохраняет тело запроса как строку <b>до</b> того, как оно будет dispose'нуто
/// клиентом, чтобы тесты могли проверить отправленный payload.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    /// <summary>Снимки запросов (с сохранённым телом).</summary>
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

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;

        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            body));

        return Task.FromResult(_handler(request));
    }

    /// <summary>Создаёт JSON-ответ.</summary>
    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>Снимок отправленного запроса с сохранённым телом.</summary>
    public sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string Body);
}