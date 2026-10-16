namespace TicketMonitor.Core.Models;

/// <summary>Изменение заявки.</summary>
public sealed record TicketChange
{
    public ChangeType Type { get; init; }
    public TicketInfo? Ticket { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>Тип изменения заявки.</summary>
public enum ChangeType
{
    New,
    Updated
}