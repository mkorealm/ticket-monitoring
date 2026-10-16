using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Реализация резолвера каналов уведомлений.</summary>
public sealed class NotificationChannelResolver(
    RocketChatNotificationService rocketChat,
    ExchangeEmailService exchangeEmail,
    ILogger<NotificationChannelResolver> logger) : INotificationChannelResolver
{
    private const string RocketChatChannelType = "RocketChat";
    private const string ExchangeChannelType = "Exchange";

    private readonly RocketChatNotificationService _rocketChat = rocketChat ?? throw new ArgumentNullException(nameof(rocketChat));
    private readonly ExchangeEmailService _exchangeEmail = exchangeEmail ?? throw new ArgumentNullException(nameof(exchangeEmail));
    private readonly ILogger<NotificationChannelResolver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public IReadOnlyList<INotificationService> ResolveForMonitor(MonitorConfig monitorConfig)
    {
        ArgumentNullException.ThrowIfNull(monitorConfig);

        var channels = monitorConfig.Notification?.Channels;
        if (channels is null || channels.Count == 0) return [];

        var resolved = new List<INotificationService>(channels.Count);
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            if (!channel.Enabled) continue;

            if (!seenTypes.Add(channel.Type))
            {
                _logger.LogWarning("Монитор '{MonitorName}': дубликат канала '{ChannelType}' пропущен", monitorConfig.Name, channel.Type);
                continue;
            }

            var service = ResolveByType(channel.Type, monitorConfig.Name);
            if (service is not null) resolved.Add(service);
        }

        return resolved;
    }

    /// <inheritdoc />
    public IReadOnlyList<INotificationService> ResolveByTargets(IReadOnlyList<string>? targets)
    {
        if (targets is null || targets.Count == 0)
            return [_rocketChat, _exchangeEmail];

        var resolved = new List<INotificationService>(targets.Count);
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target)) continue;

            if (!seenTypes.Add(target))
            {
                _logger.LogWarning("Дубликат канала '{ChannelType}' в targets пропущен", target);
                continue;
            }

            var service = ResolveByType(target, monitorName: null);
            if (service is not null) resolved.Add(service);
        }

        return resolved;
    }

    /// <inheritdoc />
    public bool ContainsRocketChat(IReadOnlyList<INotificationService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var service in services)
        {
            if (ReferenceEquals(service, _rocketChat)) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public RocketChatNotificationService GetRocketChatService() => _rocketChat;

    private INotificationService? ResolveByType(string channelType, string? monitorName)
    {
        if (string.Equals(channelType, RocketChatChannelType, StringComparison.OrdinalIgnoreCase))
            return _rocketChat;

        if (string.Equals(channelType, ExchangeChannelType, StringComparison.OrdinalIgnoreCase))
            return _exchangeEmail;

        if (monitorName is not null)
            _logger.LogWarning("Монитор '{MonitorName}': неизвестный тип канала '{ChannelType}' — пропущен", monitorName, channelType);
        else
            _logger.LogWarning("Неизвестный тип канала '{ChannelType}' — пропущен", channelType);

        return null;
    }
}