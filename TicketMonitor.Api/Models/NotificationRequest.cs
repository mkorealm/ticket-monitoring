namespace TicketMonitor.Api.Models;

/// <summary>Модель запроса для отправки уведомления.</summary>
public class NotificationRequest
{
    /// <summary>Список каналов (rocketchat, exchange). Пусто — все включённые.</summary>
    public List<string>? Targets { get; set; }

    /// <summary>Дополнительный текст сообщения.</summary>
    public string? Message { get; set; }
}