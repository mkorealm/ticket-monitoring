namespace TicketMonitor.Watchdog.Configuration;

/// <summary>Настройки Watchdog.</summary>
public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    /// <summary>Интервал HTTP-проверок (сек).</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>Интервал чтения operations.db (сек).</summary>
    public int OperationsCheckIntervalSeconds { get; set; } = 15;

    /// <summary>Сколько неудачных проверок подряд считать падением.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>Сколько дней хранить сырые HTTP-проверки.</summary>
    public int HttpRawRetentionDays { get; set; } = 30;

    /// <summary>Сколько дней хранить агрегаты HTTP.</summary>
    public int HttpAggregateRetentionDays { get; set; } = 90;

    /// <summary>Сколько дней хранить агрегаты операций.</summary>
    public int OperationsAggregateRetentionDays { get; set; } = 90;

    /// <summary>Список отслеживаемых сервисов.</summary>
    public List<MonitoredServiceConfig> Services { get; set; } = [];
}