namespace TicketMonitor.Redmine.Models;

/// <summary>Трекер Redmine (для dropdown).</summary>
public sealed record RedmineTracker
{
    /// <summary>ID трекера.</summary>
    public int Id { get; init; }

    /// <summary>Название трекера.</summary>
    public string Name { get; init; } = string.Empty;
}