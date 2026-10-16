using LiteDB;

namespace TicketMonitor.Redmine.Models;

/// <summary>Связь заявки GLPI и задачи Redmine.</summary>
public sealed record RedmineLink
{
    /// <summary>ID заявки GLPI (числовой). Первичный ключ.</summary>
    [BsonId]
    public string TicketId { get; init; } = string.Empty;

    /// <summary>ID задачи в Redmine (число из URL <c>/issues/XXX</c>).</summary>
    public int RedmineIssueId { get; init; }

    /// <summary>Человекочитаемый ID задачи в Redmine (например, DVITS-344).</summary>
    public string RedmineIssueKey { get; init; } = string.Empty;

    /// <summary>Когда связь была создана.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Кто создал задачу (логин или «система»).</summary>
    public string? CreatedBy { get; init; }
}