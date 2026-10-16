using TicketMonitor.Core.Models;
using TicketMonitor.Core.Models.Dto;

namespace TicketMonitor.Core.Contracts;

/// <summary>Интерфейс управления мониторами.</summary>
public interface IMonitorManagementService
{
    void LoadMonitorsFromDatabase();
    bool AddMonitor(MonitorConfig config);
    bool AddOrUpdateSystemMonitor(MonitorConfig config);
    bool UpdateMonitor(string monitorName, MonitorConfig newConfig);
    bool RemoveMonitor(string monitorName);
    IReadOnlyList<MonitorConfig> GetAllMonitors();
    MonitorConfig? GetMonitor(string monitorName);
    Task<bool> RunCheckAsync(string monitorName);
    MonitorStatusDto? GetStatus(string monitorName);
}