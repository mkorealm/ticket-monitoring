namespace TicketMonitor.Redmine.Models;

/// <summary>Данные формы создания задачи в Redmine.</summary>
public sealed class RedmineCreateRequest
{
    /// <summary>ID заявки GLPI.</summary>
    public string GlpiTicketId { get; set; } = string.Empty;

    /// <summary>ID проекта Redmine.</summary>
    public int ProjectId { get; set; }

    /// <summary>ID трекера.</summary>
    public int TrackerId { get; set; }

    /// <summary>Тема задачи.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Описание задачи.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>ID исполнителя в Redmine (опционально).</summary>
    public int? AssignedToId { get; set; }

    /// <summary>Значения custom fields: id поля → значение.</summary>
    public Dictionary<int, string> CustomFields { get; set; } = [];
}