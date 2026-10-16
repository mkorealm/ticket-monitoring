namespace TicketMonitor.Redmine.Models;

/// <summary>Задача Redmine (частичное представление).</summary>
public sealed record RedmineIssue
{
    /// <summary>ID задачи.</summary>
    public int Id { get; init; }

    /// <summary>Человекочитаемый ID (например, DVITS-344).</summary>
    public string? Key { get; init; }

    /// <summary>Тема.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>ID проекта.</summary>
    public int ProjectId { get; init; }

    /// <summary>ID статуса.</summary>
    public int StatusId { get; init; }

    /// <summary>Название статуса.</summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary>Признак «задача закрыта» (<c>status.is_closed = true</c>).</summary>
    public bool IsClosed { get; init; }

    /// <summary>Дата последнего обновления (UTC).</summary>
    public DateTime UpdatedOn { get; init; }

    /// <summary>Значения custom fields: id поля → значение.</summary>
    public IReadOnlyDictionary<int, string> CustomFieldValues { get; init; } = new Dictionary<int, string>();

    /// <summary>ID заявки GLPI из custom field «Номер в HD».</summary>
    public string? GlpiTicketId { get; init; }
}