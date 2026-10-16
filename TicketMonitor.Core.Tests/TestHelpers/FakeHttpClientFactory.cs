namespace TicketMonitor.Core.Tests.TestHelpers;

/// <summary>
/// Фейковая фабрика <see cref="HttpClient"/>, возвращающая клиент с заданным
/// <see cref="HttpMessageHandler"/> и корректным <see cref="HttpClient.BaseAddress"/>.
/// </summary>
public sealed class FakeHttpClientFactory(
    HttpMessageHandler handler,
    string baseAddress = "https://test.local/") : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly Uri _baseAddress = new(baseAddress);

    /// <inheritdoc />
    public HttpClient CreateClient(string name) =>
        new(_handler, disposeHandler: false)
        {
            BaseAddress = _baseAddress
        };
}