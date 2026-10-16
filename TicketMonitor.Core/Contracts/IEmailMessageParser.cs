using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>Парсер plain-text уведомлений в структурированные данные.</summary>
public interface IEmailMessageParser
{
    /// <summary>Парсит plain-text сообщение.</summary>
    EmailMessageData Parse(string plainText);
}