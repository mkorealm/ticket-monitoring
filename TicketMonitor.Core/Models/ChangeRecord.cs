namespace TicketMonitor.Core.Models;

/// <summary>Запись об изменении заявки для API.</summary>
public sealed record ChangeRecord
{
    public string Id { get; init; } = string.Empty;
    public string MonitorName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string TicketId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Initiator { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public DateTime OpenDate { get; init; }
    public DateTime LastModified { get; init; }
    public string Url { get; init; } = string.Empty;
    public DateTime DetectedAt { get; init; }
}