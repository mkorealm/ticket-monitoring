namespace TicketMonitor.Watchdog.Configuration;

/// <summary>Конфигурация одного отслеживаемого сервиса.</summary>
public sealed class MonitoredServiceConfig
{
    /// <summary>Уникальное имя сервиса (совпадает с <c>Operations:ServiceName</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Отображаемое имя (для UI).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>URL health-check эндпоинта.</summary>
    public string HealthUrl { get; set; } = string.Empty;

    /// <summary>Корневая папка сервиса (там, где лежит папка <c>Data/</c>).</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Имя файла <c>operations.db</c> в папке <c>Data/</c>.</summary>
    public string OperationsDbFile { get; set; } = "operations.db";
}