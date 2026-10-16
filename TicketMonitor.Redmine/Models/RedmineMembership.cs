namespace TicketMonitor.Redmine.Models;

/// <summary>Участник проекта Redmine (для dropdown «Назначена»).</summary>
public sealed record RedmineMembership
{
    /// <summary>ID пользователя в Redmine.</summary>
    public int UserId { get; init; }

    /// <summary>Полное имя пользователя.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Роли в проекте.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Можно ли назначать задачи на этого пользователя.</summary>
    public bool CanBeAssigned => Roles.Any(r =>
        r.Contains("Разработчик", StringComparison.OrdinalIgnoreCase) ||
        r.Contains("Менеджер", StringComparison.OrdinalIgnoreCase) ||
        r.Contains("Developer", StringComparison.OrdinalIgnoreCase) ||
        r.Contains("Manager", StringComparison.OrdinalIgnoreCase));
}