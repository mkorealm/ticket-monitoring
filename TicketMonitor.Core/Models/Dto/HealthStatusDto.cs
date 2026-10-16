namespace TicketMonitor.Core.Models.Dto;

/// <summary>Состояние сервиса.</summary>
public sealed record HealthStatusDto
{
    public string Status { get; init; } = "running";
    public string Version { get; init; } = string.Empty;
    public Dictionary<string, MonitorHealthDto> Monitors { get; init; } = [];
    public DateTime Timestamp { get; init; }
}

/// <summary>Состояние одного монитора.</summary>
public sealed record MonitorHealthDto
{
    public DateTime LastCheck { get; init; }
    public int TicketsCount { get; init; }
    public bool Stale { get; init; }
    public double StaleThresholdMinutes { get; init; }
}