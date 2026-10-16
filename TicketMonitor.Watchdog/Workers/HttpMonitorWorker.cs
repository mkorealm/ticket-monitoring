using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Models;
using TicketMonitor.Watchdog.Services;

namespace TicketMonitor.Watchdog.Workers;

/// <summary>
/// Фоновый воркер HTTP-проверок.
/// Раз в <see cref="WatchdogOptions.HealthCheckIntervalSeconds"/> проверяет health-URL каждого сервиса.
/// Успешные проверки — только в дневной агрегат. Неудачные — в сырые записи + дневной агрегат.
/// </summary>
public sealed class HttpMonitorWorker(
    WatchdogRepository repository,
    HttpHealthChecker checker,
    DowntimeTracker downtimeTracker,
    IOptions<WatchdogOptions> options,
    ILogger<HttpMonitorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);

    private readonly WatchdogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly HttpHealthChecker _checker = checker ?? throw new ArgumentNullException(nameof(checker));
    private readonly DowntimeTracker _downtimeTracker = downtimeTracker ?? throw new ArgumentNullException(nameof(downtimeTracker));
    private readonly WatchdogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<HttpMonitorWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.HealthCheckIntervalSeconds);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("HttpMonitorWorker запущен с интервалом {Interval} сек для {Count} сервисов",
                _options.HealthCheckIntervalSeconds, _options.Services.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var config in _options.Services)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                await CheckOneAsync(config, stoppingToken).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("HttpMonitorWorker остановлен");
    }

    private async Task CheckOneAsync(MonitoredServiceConfig config, CancellationToken cancellationToken)
    {
        HttpHealthCheckResult result;
        try
        {
            result = await _checker.CheckAsync(config.HealthUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка проверки сервиса {ServiceName}", config.Name);
            return;
        }

        _repository.IncrementDailyHttpStats(
            config.Name,
            totalChecks: 1,
            failedChecks: result.Healthy ? 0 : 1,
            totalResponseTimeMs: result.ResponseTimeMs ?? 0);

        if (!result.Healthy)
        {
            _repository.SaveHttpCheck(new HttpCheckRecord
            {
                ServiceName = config.Name,
                Timestamp = DateTime.UtcNow,
                StatusCode = result.StatusCode,
                ResponseTimeMs = result.ResponseTimeMs,
                ErrorMessage = result.ErrorMessage
            });
        }

        var stateChanged = _downtimeTracker.ProcessResult(config.Name, result.Healthy, result.ErrorMessage);

        if (stateChanged && _logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Состояние сервиса {ServiceName} изменилось: healthy={Healthy}", config.Name, result.Healthy);
    }
}