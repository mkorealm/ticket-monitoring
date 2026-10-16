namespace TicketMonitor.Core.Contracts;

/// <summary>Интерфейс сервиса уведомлений.</summary>
public interface INotificationService
{
    /// <summary>Отправляет уведомление.</summary>
    /// <returns>ID отправленного сообщения (если применимо), иначе <c>null</c>.</returns>
    Task<string?> SendNotificationAsync(string subject, string message, CancellationToken cancellationToken = default);
}