using System.Reflection;
using TicketMonitor.Core.Models.Dto;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис отслеживания состояния воркеров.</summary>
public class HealthService
{
    private readonly Dictionary<string, MonitorHealth> _monitorHealth = [];
    private readonly Lock _lock = new();

    /// <summary>Отмечает успешную проверку заявок.</summary>
    public void MarkCheckCompleted(int ticketsCount, string monitorName, double checkIntervalMinutes)
    {
        lock (_lock)
        {
            _monitorHealth[monitorName] = new MonitorHealth
            {
                LastCheck = DateTime.Now,
                TicketsCount = ticketsCount,
                CheckIntervalMinutes = checkIntervalMinutes
            };
        }
    }

    /// <summary>Возвращает текущее состояние сервиса.</summary>
    public HealthStatusDto GetStatus()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var versionString = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";

        lock (_lock)
        {
            var now = DateTime.Now;

            var monitors = _monitorHealth.ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    var thresholdMinutes = kvp.Value.CheckIntervalMinutes * 2;
                    var staleThreshold = TimeSpan.FromMinutes(thresholdMinutes);
                    var stale = now - kvp.Value.LastCheck > staleThreshold;

                    return new MonitorHealthDto
                    {
                        LastCheck = kvp.Value.LastCheck,
                        TicketsCount = kvp.Value.TicketsCount,
                        Stale = stale,
                        StaleThresholdMinutes = thresholdMinutes
                    };
                });

            return new HealthStatusDto
            {
                Status = "running",
                Version = versionString,
                Monitors = monitors,
                Timestamp = now
            };
        }
    }

    private class MonitorHealth
    {
        public DateTime LastCheck { get; set; }
        public int TicketsCount { get; set; }
        public double CheckIntervalMinutes { get; set; }
    }
}