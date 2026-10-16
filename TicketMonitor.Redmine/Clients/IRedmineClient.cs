using TicketMonitor.Redmine.Models;

namespace TicketMonitor.Redmine.Clients;

/// <summary>Клиент Redmine REST API.</summary>
public interface IRedmineClient
{
    /// <summary>Создаёт задачу. Возвращает созданную задачу с ID и ключом.</summary>
    Task<RedmineIssue> CreateIssueAsync(RedmineCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Возвращает задачу по ID или <c>null</c>.</summary>
    Task<RedmineIssue?> GetIssueAsync(int issueId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список проектов.</summary>
    Task<IReadOnlyList<RedmineProject>> GetProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает трекеры проекта.</summary>
    Task<IReadOnlyList<RedmineTracker>> GetTrackersAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает custom fields проекта.</summary>
    Task<IReadOnlyList<RedmineCustomField>> GetCustomFieldsAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает участников проекта.</summary>
    Task<IReadOnlyList<RedmineMembership>> GetProjectMembershipsAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает закрытые задачи, обновлённые после указанного момента (polling).</summary>
    Task<IReadOnlyList<RedmineIssue>> GetClosedIssuesAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ищет задачу в Redmine по GLPI-заявке (custom field «Номер в HD»).
    /// Возвращает <c>null</c>, если задача не найдена.
    /// </summary>
    Task<RedmineIssue?> FindIssueByGlpiTicketIdAsync(string glpiTicketId, CancellationToken cancellationToken = default);
}