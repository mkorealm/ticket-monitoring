namespace TicketMonitor.Redmine.Models;

/// <summary>Модель submit-формы создания задачи в Redmine.</summary>
public sealed class RedmineFormSubmitModel
{
    /// <summary>ID заявки GLPI, по которой создаётся задача.</summary>
    public string GlpiTicketId { get; set; } = string.Empty;

    /// <summary>ID выбранного проекта Redmine.</summary>
    public int ProjectId { get; set; }

    /// <summary>ID выбранного трекера.</summary>
    public int TrackerId { get; set; }

    /// <summary>Тема задачи.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Описание задачи.</summary>
    public string? Description { get; set; }

    /// <summary>ID исполнителя в Redmine (опционально).</summary>
    public int? AssignedToId { get; set; }

    /// <summary>Значения custom fields: id поля (строкой) → значение.</summary>
    public Dictionary<string, string>? CustomFields { get; set; }

    /// <summary>
    /// Признак «Всё равно создать» — игнорирует существующую связь и создаёт вторую задачу.
    /// </summary>
    public bool ForceCreate { get; set; }
}