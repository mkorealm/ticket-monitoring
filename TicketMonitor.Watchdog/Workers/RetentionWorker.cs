using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Services;

namespace TicketMonitor.Watchdog.Workers;

/// <summary>
/// Фоновый воркер чистки старых записей в watchdog.db.
/// Раз в сутки удаляет записи старше retention-политики.
/// </summary>
public sealed class RetentionWorker(
    WatchdogRepository repository,
    IOptions<WatchdogOptions> options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly WatchdogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly WatchdogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RetentionWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Cleanup();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чистки старых записей Watchdog");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Cleanup()
    {
        var deleted = _repository.ApplyRetention(
            httpRawRetentionDays: _options.HttpRawRetentionDays,
            httpAggregateRetentionDays: _options.HttpAggregateRetentionDays,
            operationsAggregateRetentionDays: _options.OperationsAggregateRetentionDays);

        if (deleted == 0 && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Retention: нечего удалять");
    }
}