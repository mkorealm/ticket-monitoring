using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Models;

namespace TicketMonitor.Watchdog.Services;

/// <summary>
/// Отслеживает падения и восстановления сервисов.
/// Держит счётчик последовательных неудач в памяти; в БД пишет только при переходе через порог.
/// </summary>
public sealed class DowntimeTracker(
    WatchdogRepository repository,
    IOptions<WatchdogOptions> options,
    ILogger<DowntimeTracker> logger)
{
    private readonly WatchdogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly WatchdogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<DowntimeTracker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// Обрабатывает результат проверки.
    /// Возвращает <c>true</c>, если состояние изменилось (открылось или закрылось падение).
    /// </summary>
    public bool ProcessResult(string serviceName, bool healthy, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        lock (_consecutiveFailures)
        {
            if (!_consecutiveFailures.TryGetValue(serviceName, out var failures))
                failures = 0;

            if (healthy)
            {
                _consecutiveFailures[serviceName] = 0;

                var open = _repository.GetOpenDowntime(serviceName);
                if (open is null)
                    return false;

                var endUtc = DateTime.UtcNow;
                var startUtc = DateTime.SpecifyKind(open.StartTime, DateTimeKind.Utc);

                var durationMinutes = (endUtc - startUtc).TotalMinutes;

                if (durationMinutes < 0)
                {
                    _logger.LogWarning(
                        "Заявка о падении сервиса {ServiceName} имеет отрицательную длительность ({Duration:F1} мин). " +
                        "StartTime={StartTime}, EndTime={EndTime}. Значение будет зажато в 0",
                        serviceName, durationMinutes, open.StartTime, endUtc);
                    durationMinutes = 0;
                }

                open.EndTime = endUtc;
                open.DurationMinutes = durationMinutes;
                _repository.SaveDowntime(open);

                _repository.AddDailyDowntimeMinutes(serviceName, durationMinutes);

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation(
                        "Сервис {ServiceName} восстановлен после {Duration:F1} мин простоя",
                        serviceName, durationMinutes);

                return true;
            }

            failures++;
            _consecutiveFailures[serviceName] = failures;

            if (failures < _options.FailureThreshold)
                return false;

            if (_repository.GetOpenDowntime(serviceName) is not null)
                return false;

            var evt = new DowntimeEvent
            {
                ServiceName = serviceName,
                StartTime = DateTime.UtcNow,
                Reason = reason
            };

            _repository.SaveDowntime(evt);

            _logger.LogWarning(
                "Сервис {ServiceName} упал (после {Failures} неудачных проверок): {Reason}",
                serviceName, failures, reason ?? "(без причины)");

            return true;
        }
    }
}