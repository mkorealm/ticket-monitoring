using TicketMonitor.Core.Models;

namespace TicketMonitor.Redmine.Models;

/// <summary>ViewModel для формы создания задачи в Redmine.</summary>
public sealed class RedmineFormViewModel
{
    /// <summary>Заявка GLPI, по которой создаётся задача.</summary>
    public required TicketInfo Ticket { get; init; }

    /// <summary>Доступные проекты Redmine.</summary>
    public IReadOnlyList<RedmineProject> Projects { get; init; } = [];

    /// <summary>Трекеры выбранного проекта.</summary>
    public IReadOnlyList<RedmineTracker> Trackers { get; init; } = [];

    /// <summary>Участники выбранного проекта (для dropdown «Назначена»).</summary>
    public IReadOnlyList<RedmineMembership> Memberships { get; init; } = [];

    /// <summary>Выбранный проект (по умолчанию — DefaultProjectId).</summary>
    public int SelectedProjectId { get; init; }

    /// <summary>Выбранный трекер (по умолчанию — DefaultTrackerId).</summary>
    public int SelectedTrackerId { get; init; }

    /// <summary>URL задачи в GLPI.</summary>
    public string GlpiTicketUrl => Ticket.Url;

    /// <summary>Признак, что заявка уже связана с задачей Redmine.</summary>
    public bool IsAlreadyLinked { get; init; }

    /// <summary>ID существующей задачи Redmine, если связана.</summary>
    public int? ExistingRedmineIssueId { get; init; }

    /// <summary>URL существующей задачи Redmine, если связана.</summary>
    public string? ExistingRedmineIssueUrl { get; init; }

    /// <summary>Текущее значение поля «Внешний ID» (externalid) заявки GLPI, если известно.</summary>
    public string? ExistingGlpiExternalId { get; init; }

    /// <summary>Признак принудительного создания второй задачи (кнопка «Всё равно создать»).</summary>
    public bool ForceCreate { get; init; }
}