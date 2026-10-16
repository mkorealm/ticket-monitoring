using LiteDB;
using Microsoft.Extensions.Logging;
using TicketMonitor.Watchdog.Models;

namespace TicketMonitor.Watchdog.Services;

/// <summary>Репозиторий LiteDB для статистики Watchdog.</summary>
public class WatchdogRepository : IDisposable
{
    private const string HttpChecksCollection = "httpChecks";
    private const string DowntimesCollection = "downtimes";
    private const string DailyHttpStatsCollection = "dailyHttpStats";
    private const string DailyOperationStatsCollection = "dailyOperationStats";
    private const string ServiceReadStateCollection = "serviceReadState";

    private readonly ILogger<WatchdogRepository> _logger;
    private readonly string _dbPath;
    private readonly Lock _lock = new();
    private LiteDatabase? _db;
    private bool _disposed;

    /// <summary>Инициализирует репозиторий.</summary>
    public WatchdogRepository(ILogger<WatchdogRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "watchdog.db");
    }

    // -------------------------------------------------------------------------
    // HttpCheckRecord
    // -------------------------------------------------------------------------

    /// <summary>Сохраняет сырую запись HTTP-проверки (только для неудач).</summary>
    public void SaveHttpCheck(HttpCheckRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<HttpCheckRecord>(HttpChecksCollection);
            collection.EnsureIndex(x => x.Timestamp);
            collection.Insert(record);
        }
    }

    // -------------------------------------------------------------------------
    // DowntimeEvent
    // -------------------------------------------------------------------------

    /// <summary>Возвращает открытое событие падения для сервиса, если есть.</summary>
    public virtual DowntimeEvent? GetOpenDowntime(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DowntimeEvent>(DowntimesCollection);
            collection.EnsureIndex(x => x.ServiceName);
            collection.EnsureIndex(x => x.StartTime);
            return collection.FindOne(x => x.ServiceName == serviceName && x.EndTime == null);
        }
    }

    /// <summary>Сохраняет событие падения (insert или update).</summary>
    public virtual void SaveDowntime(DowntimeEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DowntimeEvent>(DowntimesCollection);
            collection.EnsureIndex(x => x.ServiceName);
            collection.EnsureIndex(x => x.StartTime);
            collection.Upsert(evt);
        }
    }

    /// <summary>Возвращает список событий падения за период.</summary>
    public IReadOnlyList<DowntimeEvent> GetDowntimes(DateTime fromUtc, DateTime toUtc)
    {
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DowntimeEvent>(DowntimesCollection);
            return [.. collection
                .Find(x => x.StartTime >= fromUtc && x.StartTime <= toUtc)
                .OrderByDescending(x => x.StartTime)];
        }
    }

    // -------------------------------------------------------------------------
    // DailyHttpStats
    // -------------------------------------------------------------------------

    /// <summary>Инкрементирует дневной агрегат HTTP-проверок.</summary>
    public void IncrementDailyHttpStats(
        string serviceName,
        long totalChecks,
        long failedChecks,
        double totalResponseTimeMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var date = DateTime.UtcNow.Date;
        var id = BuildDailyHttpStatsId(date, serviceName);

        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyHttpStats>(DailyHttpStatsCollection);
            collection.EnsureIndex(x => x.Date);
            collection.EnsureIndex(x => x.ServiceName);

            var stats = collection.FindById(id);
            if (stats is null)
            {
                stats = new DailyHttpStats
                {
                    Id = id,
                    Date = date,
                    ServiceName = serviceName,
                    TotalChecks = totalChecks,
                    FailedChecks = failedChecks,
                    TotalResponseTimeMs = totalResponseTimeMs
                };
                collection.Insert(stats);
            }
            else
            {
                stats.TotalChecks += totalChecks;
                stats.FailedChecks += failedChecks;
                stats.TotalResponseTimeMs += totalResponseTimeMs;
                collection.Update(stats);
            }
        }
    }

    /// <summary>Добавляет минуты простоя к дневному агрегату.</summary>
    public virtual void AddDailyDowntimeMinutes(string serviceName, double minutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (minutes <= 0) return;

        var date = DateTime.UtcNow.Date;
        var id = BuildDailyHttpStatsId(date, serviceName);

        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyHttpStats>(DailyHttpStatsCollection);
            var stats = collection.FindById(id);
            if (stats is null)
            {
                stats = new DailyHttpStats
                {
                    Id = id,
                    Date = date,
                    ServiceName = serviceName,
                    TotalDowntimeMinutes = minutes
                };
                collection.Insert(stats);
            }
            else
            {
                stats.TotalDowntimeMinutes += minutes;
                collection.Update(stats);
            }
        }
    }

    /// <summary>Возвращает агрегаты HTTP за период.</summary>
    public IReadOnlyList<DailyHttpStats> GetDailyHttpStats(DateTime fromDate, DateTime toDate)
    {
        var from = fromDate.Date;
        var to = toDate.Date;

        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyHttpStats>(DailyHttpStatsCollection);
            return [.. collection
                .Find(x => x.Date >= from && x.Date <= to)
                .OrderBy(x => x.Date)];
        }
    }

    // -------------------------------------------------------------------------
    // DailyOperationStats
    // -------------------------------------------------------------------------

    /// <summary>Инкрементирует дневной агрегат операций.</summary>
    public virtual void IncrementDailyOperationStats(
        string serviceName,
        string type,
        string status,
        long count,
        double totalDurationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        if (count <= 0) return;

        var date = DateTime.UtcNow.Date;
        var id = BuildDailyOperationStatsId(date, serviceName, type, status);

        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyOperationStats>(DailyOperationStatsCollection);
            collection.EnsureIndex(x => x.Date);
            collection.EnsureIndex(x => x.ServiceName);
            collection.EnsureIndex(x => x.Type);

            var stats = collection.FindById(id);
            if (stats is null)
            {
                stats = new DailyOperationStats
                {
                    Id = id,
                    Date = date,
                    ServiceName = serviceName,
                    Type = type,
                    Status = status,
                    Count = count,
                    TotalDurationMs = totalDurationMs
                };
                collection.Insert(stats);
            }
            else
            {
                stats.Count += count;
                stats.TotalDurationMs += totalDurationMs;
                collection.Update(stats);
            }
        }
    }

    /// <summary>Возвращает агрегаты операций за период.</summary>
    public IReadOnlyList<DailyOperationStats> GetDailyOperationStats(DateTime fromDate, DateTime toDate)
    {
        var from = fromDate.Date;
        var to = toDate.Date;

        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyOperationStats>(DailyOperationStatsCollection);
            return [.. collection
                .Find(x => x.Date >= from && x.Date <= to)
                .OrderBy(x => x.Date)];
        }
    }

    // -------------------------------------------------------------------------
    // ServiceReadState
    // -------------------------------------------------------------------------

    /// <summary>Возвращает состояние чтения operations.db для сервиса.</summary>
    public ServiceReadState? GetReadState(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<ServiceReadState>(ServiceReadStateCollection);
            return collection.FindById(serviceName);
        }
    }

    /// <summary>Сохраняет состояние чтения operations.db.</summary>
    public void SaveReadState(ServiceReadState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ServiceName);

        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<ServiceReadState>(ServiceReadStateCollection);
            collection.Upsert(state);
        }
    }

    // -------------------------------------------------------------------------
    // Retention
    // -------------------------------------------------------------------------

    /// <summary>Удаляет устаревшие записи. Возвращает суммарное количество удалённых.</summary>
    public int ApplyRetention(
        int httpRawRetentionDays,
        int httpAggregateRetentionDays,
        int operationsAggregateRetentionDays)
    {
        var now = DateTime.UtcNow;
        var httpRawCutoff = now.AddDays(-httpRawRetentionDays);
        var httpAggCutoff = now.AddDays(-httpAggregateRetentionDays).Date;
        var opsAggCutoff = now.AddDays(-operationsAggregateRetentionDays).Date;

        lock (_lock)
        {
            var db = GetDatabase();

            var httpChecks = db.GetCollection<HttpCheckRecord>(HttpChecksCollection)
                .DeleteMany(x => x.Timestamp < httpRawCutoff);

            var downtimes = db.GetCollection<DowntimeEvent>(DowntimesCollection)
                .DeleteMany(x => x.StartTime < httpRawCutoff);

            var dailyHttp = db.GetCollection<DailyHttpStats>(DailyHttpStatsCollection)
                .DeleteMany(x => x.Date < httpAggCutoff);

            var dailyOps = db.GetCollection<DailyOperationStats>(DailyOperationStatsCollection)
                .DeleteMany(x => x.Date < opsAggCutoff);

            var total = httpChecks + downtimes + dailyHttp + dailyOps;

            if (total > 0 && _logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "Retention: удалено {Total} записей (httpChecks={Http}, downtimes={Downtime}, dailyHttp={DailyHttp}, dailyOps={DailyOps})",
                    total, httpChecks, downtimes, dailyHttp, dailyOps);

            return total;
        }
    }

    /// <summary>Возвращает агрегаты HTTP за конкретный день (UTC).</summary>
    public IReadOnlyList<DailyHttpStats> GetDailyHttpStatsForDay(DateTime date)
    {
        var day = date.Date;
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyHttpStats>(DailyHttpStatsCollection);
            return [.. collection.Find(x => x.Date == day)];
        }
    }

    /// <summary>Возвращает агрегаты операций за конкретный день (UTC).</summary>
    public IReadOnlyList<DailyOperationStats> GetDailyOperationStatsForDay(DateTime date)
    {
        var day = date.Date;
        lock (_lock)
        {
            var collection = GetDatabase().GetCollection<DailyOperationStats>(DailyOperationStatsCollection);
            return [.. collection.Find(x => x.Date == day)];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _db?.Dispose();
            _db = null;
        }

        _disposed = true;
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private LiteDatabase GetDatabase()
    {
        if (_db is not null)
            return _db;

        var connectionString = $"Filename={_dbPath};Connection=shared";
        _db = new LiteDatabase(connectionString);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Открыта БД Watchdog: {DbPath}", _dbPath);

        return _db;
    }

    private static string BuildDailyHttpStatsId(DateTime date, string serviceName)
        => $"{date:yyyy-MM-dd}_{serviceName}";

    private static string BuildDailyOperationStatsId(DateTime date, string serviceName, string type, string status)
        => $"{date:yyyy-MM-dd}_{serviceName}_{type}_{status}";
}