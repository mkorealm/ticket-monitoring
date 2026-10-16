namespace TicketMonitor.Core.Models;

/// <summary>Конфигурация каналов уведомлений для монитора.</summary>
public class NotificationChannelsConfig
{
    public List<NotificationChannelConfig> Channels { get; set; } = [];
}

/// <summary>Конфигурация одного канала уведомлений.</summary>
public class NotificationChannelConfig
{
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = [];
}

/// <summary>
/// Конфигурация монитора.
/// <b>Важно:</b> это изменяемый класс (не record).
/// Экземпляры хранятся в <see cref="Background.MonitorRuntime"/> и
/// возвращаются как «живые» ссылки через <see cref="Contracts.IMonitorManagementService.GetAllMonitors"/>.
/// Если нужно изменить — клонируйте или используйте методы сервиса.
/// </summary>
public class MonitorConfig
{
    public string Name { get; set; } = string.Empty;
    public string CategoryFilter { get; set; } = string.Empty;
    public double CheckIntervalMinutes { get; set; } = 5;
    public string DatabaseFileName { get; set; } = "tickets.db";
    public int SupportGroupId { get; set; }
    public bool IsSystem { get; set; }
    public NotificationChannelsConfig Notification { get; set; } = new();
}