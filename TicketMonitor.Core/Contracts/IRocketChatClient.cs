namespace TicketMonitor.Core.Contracts;

/// <summary>Низкоуровневый клиент Rocket.Chat для отправки сообщений через REST API.</summary>
public interface IRocketChatClient
{
    /// <summary>Отправляет сообщение в чат.</summary>
    /// <param name="roomId">ID комнаты.</param>
    /// <param name="text">Текст сообщения.</param>
    /// <param name="threadMessageId">ID родительского сообщения или <c>null</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>ID отправленного сообщения или <c>null</c> при ошибке.</returns>
    Task<string?> SendMessageAsync(
        string roomId,
        string text,
        string? threadMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>Редактирует текст существующего сообщения.</summary>
    /// <param name="roomId">ID комнаты.</param>
    /// <param name="messageId">ID редактируемого сообщения.</param>
    /// <param name="newText">Новый текст сообщения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если сообщение успешно отредактировано; иначе <c>false</c>.</returns>
    Task<bool> UpdateMessageAsync(
        string roomId,
        string messageId,
        string newText,
        CancellationToken cancellationToken = default);
}