using LiteDB;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Models;
using TicketMonitor.Watchdog.Models;

namespace TicketMonitor.Watchdog.Services;

/// <summary>Читает <c>operations.db</c> сервисов (read-only).</summary>
public sealed class OperationsDbReader(ILogger<OperationsDbReader> logger)
{
    private readonly ILogger<OperationsDbReader> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Возвращает операции, появившиеся после <paramref name="sinceUtc"/>.
    /// Если <paramref name="sinceUtc"/> == <c>null</c> — возвращает только те, что за последний час.
    /// </summary>
    public IReadOnlyList<OperationRecord> ReadNewOperations(MonitoredService service, DateTime? sinceUtc)
    {
        ArgumentNullException.ThrowIfNull(service);

        var dbPath = service.OperationsDbPath;
        if (!File.Exists(dbPath))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("operations.db не найден: {DbPath}", dbPath);
            return [];
        }

        var cutoff = sinceUtc ?? DateTime.UtcNow.AddHours(-1);

        try
        {
            using var db = new LiteDatabase($"Filename={dbPath};Connection=shared");
            var collection = db.GetCollection<OperationRecord>("operations");
            collection.EnsureIndex(x => x.StartedAt);

            return [.. collection
                .Find(x => x.StartedAt > cutoff)
                .OrderBy(x => x.StartedAt)];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать operations.db сервиса {ServiceName}: {DbPath}", service.Name, dbPath);
            return [];
        }
    }
}