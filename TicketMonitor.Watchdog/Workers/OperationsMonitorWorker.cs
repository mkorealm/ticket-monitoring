using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Models;
using TicketMonitor.Watchdog.Services;

namespace TicketMonitor.Watchdog.Workers;

/// <summary>
/// Фоновый воркер чтения operations.db.
/// Раз в <see cref="WatchdogOptions.OperationsCheckIntervalSeconds"/> читает новые операции
/// каждого сервиса и агрегирует их в дневные срезы.
/// </summary>
public sealed class OperationsMonitorWorker(
    OperationsDbReader reader,
    OperationsAggregator aggregator,
    WatchdogRepository repository,
    IOptions<WatchdogOptions> options,
    ILogger<OperationsMonitorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);

    private readonly OperationsDbReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly OperationsAggregator _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
    private readonly WatchdogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly WatchdogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<OperationsMonitorWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

        var interval = TimeSpan.FromSeconds(_options.OperationsCheckIntervalSeconds);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("OperationsMonitorWorker запущен с интервалом {Interval} сек для {Count} сервисов",
                _options.OperationsCheckIntervalSeconds, _options.Services.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var config in _options.Services)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                ProcessOne(config);
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

        _logger.LogInformation("OperationsMonitorWorker остановлен");
    }

    private void ProcessOne(MonitoredServiceConfig config)
    {
        try
        {
            var service = MonitoredService.FromConfig(config);

            var state = _repository.GetReadState(service.Name);
            var since = state?.LastProcessedStartedAt;

            var operations = _reader.ReadNewOperations(service, since);
            if (operations.Count == 0)
                return;

            _aggregator.Aggregate(service.Name, operations);

            var maxStartedAt = operations.Max(x => x.StartedAt);
            _repository.SaveReadState(new ServiceReadState
            {
                ServiceName = service.Name,
                LastProcessedStartedAt = maxStartedAt
            });

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Сервис {ServiceName}: прочитано {Count} операций (since={Since})",
                    service.Name, operations.Count, since);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки operations.db сервиса {ServiceName}", config.Name);
        }
    }
}