using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TicketMonitor.Watchdog.Services;

/// <summary>Результат HTTP-проверки.</summary>
public sealed record HttpHealthCheckResult
{
    /// <summary>Сервис доступен.</summary>
    public required bool Healthy { get; init; }

    /// <summary>HTTP-статус (если получен ответ).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Время ответа (мс).</summary>
    public double? ResponseTimeMs { get; init; }

    /// <summary>Текст ошибки (если не удалось или статус не 2xx).</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Проверяет HTTP health-эндпоинты сервисов.</summary>
public sealed class HttpHealthChecker(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpHealthChecker> logger)
{
    private const int TimeoutSeconds = 10;

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<HttpHealthChecker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Проверяет health-URL сервиса.</summary>
    public async Task<HttpHealthCheckResult> CheckAsync(string healthUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(healthUrl);

        var sw = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

            using var response = await client.GetAsync(healthUrl, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new HttpHealthCheckResult
                {
                    Healthy = true,
                    StatusCode = (int)response.StatusCode,
                    ResponseTimeMs = sw.Elapsed.TotalMilliseconds
                };
            }

            return new HttpHealthCheckResult
            {
                Healthy = false,
                StatusCode = (int)response.StatusCode,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new HttpHealthCheckResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = $"Timeout ({TimeoutSeconds}s)"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "HTTP-проверка не удалась: {Url}", healthUrl);
            return new HttpHealthCheckResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}