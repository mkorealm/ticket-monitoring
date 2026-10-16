namespace TicketMonitor.Core.Contracts;

/// <summary>Проверка наличия связи заявки GLPI с задачей Redmine.</summary>
public interface IRedmineLinkChecker
{
    /// <summary>Проверяет, создана ли по заявке GLPI задача в Redmine.</summary>
    /// <param name="glpiTicketId">ID заявки GLPI.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если задача уже создана; иначе <c>false</c>.</returns>
    Task<bool> IsLinkedAsync(string glpiTicketId, CancellationToken cancellationToken = default);
}