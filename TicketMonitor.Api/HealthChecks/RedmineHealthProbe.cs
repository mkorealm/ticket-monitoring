using Microsoft.Extensions.Options;
using System.Diagnostics;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;

namespace TicketMonitor.Api.HealthChecks;

/// <summary>Проверка доступности Redmine-сервиса.</summary>
public sealed class RedmineHealthProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<RedmineLinkCheckerOptions> options,
    ILogger<RedmineHealthProbe> logger) : IExternalServiceProbe
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly RedmineLinkCheckerOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RedmineHealthProbe> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string ServiceName => "Redmine";

    /// <inheritdoc />
    public async Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = 0,
                ErrorMessage = "BaseUrl не настроен"
            };
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient(RedmineLinkChecker.HttpClientName);
            using var response = await client.GetAsync("health", cancellationToken).ConfigureAwait(false);

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
            _logger.LogWarning(ex, "Redmine health probe failed");

            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}