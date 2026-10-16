using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Models;
using TicketMonitor.Watchdog.Models;

namespace TicketMonitor.Watchdog.Services;

/// <summary>Агрегирует операции из operations.db в дневные срезы.</summary>
public sealed class OperationsAggregator(
    WatchdogRepository repository,
    ILogger<OperationsAggregator> logger)
{
    private readonly WatchdogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<OperationsAggregator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Группирует операции по (Type, Status) и инкрементирует дневные агрегаты.
    /// Возвращает количество обработанных операций.
    /// </summary>
    public int Aggregate(string serviceName, IReadOnlyList<OperationRecord> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (operations.Count == 0)
            return 0;

        var groups = new Dictionary<(string Type, string Status), (long Count, double TotalMs)>();

        foreach (var op in operations)
        {
            var type = string.IsNullOrWhiteSpace(op.Type) ? "Unknown" : op.Type;
            var status = string.IsNullOrWhiteSpace(op.Status) ? "Unknown" : op.Status;
            var duration = op.DurationMs ?? 0;

            var key = (type, status);
            if (groups.TryGetValue(key, out var acc))
                groups[key] = (acc.Count + 1, acc.TotalMs + duration);
            else
                groups[key] = (1, duration);
        }

        foreach (var ((type, status), (count, totalMs)) in groups)
        {
            _repository.IncrementDailyOperationStats(serviceName, type, status, count, totalMs);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Сервис {ServiceName}: агрегировано {Count} операций в {Groups} групп",
                serviceName, operations.Count, groups.Count);

        return operations.Count;
    }
}