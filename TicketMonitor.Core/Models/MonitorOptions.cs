namespace TicketMonitor.Core.Models;

/// <summary>Настройки мониторов из конфигурации.</summary>
public class MonitorOptions
{
    public List<MonitorConfig> Monitors { get; set; } = [];
}