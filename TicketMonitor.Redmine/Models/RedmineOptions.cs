namespace TicketMonitor.Redmine.Models;

/// <summary>Настройки интеграции с Redmine.</summary>
public sealed class RedmineOptions
{
    public const string SectionName = "Redmine";

    /// <summary>Базовый URL Redmine без trailing slash.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API-токен сервисной учётки (env-переменная <c>REDMINE__ApiToken</c>).</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>ID проектов, для которых форма полностью поддерживается (custom fields известны).</summary>
    public int[] AllowedProjectIds { get; set; } = [18, 46];

    /// <summary>ID проекта по умолчанию (18 = dv-its-asuo).</summary>
    public int DefaultProjectId { get; set; } = 18;

    /// <summary>ID трекера по умолчанию (3 = Поддержка).</summary>
    public int DefaultTrackerId { get; set; } = 3;

    /// <summary>ID статуса при создании (1 = Новая).</summary>
    public int DefaultStatusId { get; set; } = 1;

    /// <summary>ID приоритета (2 = Нормальный).</summary>
    public int DefaultPriorityId { get; set; } = 2;

    /// <summary>Значение по умолчанию для custom field «Среда разработки».</summary>
    public string DefaultDevelopmentEnvironment { get; set; } = "OTHER";

    /// <summary>Значение по умолчанию для custom field «Техдолг».</summary>
    public bool DefaultTechDebt { get; set; } = true;

    /// <summary>Интервал polling в минутах.</summary>
    public int PollingIntervalMinutes { get; set; } = 5;

    /// <summary>Базовый URL нашей формы создания задачи.</summary>
    public string FormBaseUrl { get; set; } = string.Empty;

    /// <summary>ID custom field «Номер в HD» (пишем числовой GLPI ID).</summary>
    public int CustomFieldHdNumber { get; set; } = 5;

    /// <summary>ID custom field «Техдолг».</summary>
    public int CustomFieldTechDebt { get; set; } = 7;

    /// <summary>ID custom field «Среда разработки».</summary>
    public int CustomFieldDevEnvironment { get; set; } = 13;

    /// <summary>ID статусов, считающихся «закрытыми» для polling.</summary>
    public int[] PollingClosedStatusIds { get; set; } = [3, 5, 6, 11, 12];

    /// <summary>Полный URL задачи в Redmine.</summary>
    public string GetIssueUrl(int issueId) => $"{BaseUrl.TrimEnd('/')}/issues/{issueId}";

    /// <summary>URL нашей формы создания задачи для указанной заявки GLPI.</summary>
    public string GetCreateFormUrl(string glpiTicketId) => $"{FormBaseUrl}?ticketId={glpiTicketId}";
}