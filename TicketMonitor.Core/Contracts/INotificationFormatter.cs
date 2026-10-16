using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>
/// Форматтер уведомления в конкретный транспортный формат
/// (HTML для email, Markdown для Rocket.Chat).
/// </summary>
public interface INotificationFormatter
{
    /// <summary>Идентификатор формата (для диагностики и логов).</summary>
    string FormatName { get; }

    /// <summary>Форматирует уведомление в строку.</summary>
    string Format(NotificationMessage message);
}