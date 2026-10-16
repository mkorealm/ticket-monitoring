using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>Сервис формирования текстовых сообщений для уведомлений по заявкам.</summary>
public interface IMessageBuilderService
{
    /// <summary>Строит сообщение для новой заявки.</summary>
    string BuildForNewTicket(TicketInfo ticket);

    /// <summary>Строит сообщение для обновлённой заявки.</summary>
    string BuildForUpdatedTicket(TicketInfo ticket, IReadOnlyList<string> deltas);
}