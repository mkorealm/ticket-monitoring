using TicketMonitor.Watchdog.Configuration;

namespace TicketMonitor.Watchdog.Models;

/// <summary>Runtime-модель отслеживаемого сервиса.</summary>
public sealed class MonitoredService
{
    /// <summary>Уникальное имя сервиса.</summary>
    public required string Name { get; init; }

    /// <summary>Отображаемое имя.</summary>
    public required string DisplayName { get; init; }

    /// <summary>URL health-check.</summary>
    public required string HealthUrl { get; init; }

    /// <summary>Корневая папка сервиса.</summary>
    public required string BasePath { get; init; }

    /// <summary>Имя файла operations.db.</summary>
    public required string OperationsDbFile { get; init; }

    /// <summary>Полный путь к <c>operations.db</c>.</summary>
    public string OperationsDbPath => Path.Combine(BasePath, "Data", OperationsDbFile);

    /// <summary>Создаёт runtime-модель из конфига.</summary>
    public static MonitoredService FromConfig(MonitoredServiceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new MonitoredService
        {
            Name = config.Name,
            DisplayName = config.DisplayName,
            HealthUrl = config.HealthUrl,
            BasePath = config.BasePath,
            OperationsDbFile = config.OperationsDbFile
        };
    }
}