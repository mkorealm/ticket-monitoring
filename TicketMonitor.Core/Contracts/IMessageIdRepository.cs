namespace TicketMonitor.Core.Contracts;

/// <summary>Репозиторий для хранения и получения MessageId по заявкам.</summary>
public interface IMessageIdRepository
{
    /// <summary>Возвращает сохранённый MessageId для заявки или <c>null</c>.</summary>
    string? GetMessageId(string ticketId, string dbFileName);

    /// <summary>Сохраняет MessageId для заявки (upsert).</summary>
    void SaveMessageId(string ticketId, string messageId, string dbFileName);
}