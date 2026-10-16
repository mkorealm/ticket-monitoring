using System.Diagnostics;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Api.HealthChecks;

/// <summary>Проверка доступности Rocket.Chat.</summary>
public sealed class RocketChatHealthProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<RocketChatOptions> options,
    ILogger<RocketChatHealthProbe> logger) : IExternalServiceProbe
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly RocketChatOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RocketChatHealthProbe> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string ServiceName => "RocketChat";

    /// <inheritdoc />
    public async Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ServerUrl))
        {
            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = 0,
                ErrorMessage = "ServerUrl не настроен"
            };
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient("rocketchat");
            using var response = await client.GetAsync("api/v1/info", cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ProbeResult
                {
                    Healthy = true,
                    ResponseTimeMs = sw.Elapsed.TotalMilliseconds
                };
            }

            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "RocketChat health probe failed");

            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}