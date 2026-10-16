namespace TicketMonitor.Core.Models;

/// <summary>Информация о заявке.</summary>
public sealed record TicketInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime OpenDate { get; init; }
    public string Initiator { get; init; } = string.Empty;
    public string Assignee { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
    public int Position { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? MessageId { get; init; }
}