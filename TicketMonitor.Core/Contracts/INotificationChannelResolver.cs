using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;

namespace TicketMonitor.Core.Contracts;

/// <summary>Резолвер активных каналов уведомлений для монитора.</summary>
public interface INotificationChannelResolver
{
    IReadOnlyList<INotificationService> ResolveForMonitor(MonitorConfig monitorConfig);
    IReadOnlyList<INotificationService> ResolveByTargets(IReadOnlyList<string>? targets);
    bool ContainsRocketChat(IReadOnlyList<INotificationService> services);
    RocketChatNotificationService GetRocketChatService();
}