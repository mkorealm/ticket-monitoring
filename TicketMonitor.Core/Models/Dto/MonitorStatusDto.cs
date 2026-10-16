namespace TicketMonitor.Core.Models.Dto;

/// <summary>Статус одного монитора.</summary>
public sealed record MonitorStatusDto
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public DateTime LastCheck { get; init; }
    public int TicketsCount { get; init; }
    public double IntervalMinutes { get; init; }
}