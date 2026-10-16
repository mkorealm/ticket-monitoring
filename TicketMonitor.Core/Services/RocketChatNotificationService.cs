using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис отправки уведомлений в Rocket.Chat.</summary>
public class RocketChatNotificationService(
    IRocketChatClient client,
    IOptions<RocketChatOptions> options,
    ILogger<RocketChatNotificationService> logger) : INotificationService
{
    private readonly IRocketChatClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly RocketChatOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RocketChatNotificationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task<string?> SendNotificationAsync(string subject, string message, CancellationToken cancellationToken = default)
        => SendThreadedAsync(subject, message, monitorName: null, tmid: null, cancellationToken);

    /// <summary>Отправляет сообщение в Rocket.Chat с опциональной привязкой к треду.</summary>
    public async Task<string?> SendThreadedAsync(
        string subject,
        string message,
        string? monitorName,
        string? tmid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var roomId = GetRoomId(monitorName);
        if (string.IsNullOrEmpty(roomId))
        {
            _logger.LogWarning("RoomId не настроен для монитора '{MonitorName}' — сообщение не отправлено", monitorName);
            return null;
        }

        var messageId = await _client
            .SendMessageAsync(roomId, message, tmid, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(messageId) && _logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Сообщение отправлено в Rocket.Chat для монитора '{MonitorName}': {Subject}", monitorName, subject);

        return messageId;
    }

    /// <summary>Редактирует корневое сообщение в Rocket.Chat.</summary>
    public async Task<bool> EditRootMessageAsync(
        string messageId,
        string newText,
        string? monitorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newText);

        var roomId = GetRoomId(monitorName);
        if (string.IsNullOrEmpty(roomId))
        {
            _logger.LogWarning("RoomId не настроен для монитора '{MonitorName}' — сообщение не отредактировано", monitorName);
            return false;
        }

        return await _client
            .UpdateMessageAsync(roomId, messageId, newText, cancellationToken)
            .ConfigureAwait(false);
    }

    private string? GetRoomId(string? monitorName)
    {
        if (!string.IsNullOrEmpty(monitorName)
            && _options.RoomIds.TryGetValue(monitorName, out var roomId)
            && !string.IsNullOrEmpty(roomId))
            return roomId;

        return _options.DefaultRoomId;
    }
}