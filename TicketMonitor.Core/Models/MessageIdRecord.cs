using LiteDB;

namespace TicketMonitor.Core.Models;

/// <summary>Запись для хранения MessageId (например, ID сообщения в Rocket.Chat) в отдельной коллекции.</summary>
public sealed class MessageIdRecord
{
    /// <summary>ID заявки (первичный ключ).</summary>
    [BsonId]
    public string TicketId { get; set; } = string.Empty;

    /// <summary>ID сообщения во внешней системе.</summary>
    public string MessageId { get; set; } = string.Empty;
}