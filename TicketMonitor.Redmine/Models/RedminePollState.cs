using LiteDB;

namespace TicketMonitor.Redmine.Models;

/// <summary>Состояние polling: время последней успешной синхронизации.</summary>
public sealed record RedminePollState
{
    /// <summary>Фиксированный ключ (всегда <c>"singleton"</c>).</summary>
    [BsonId]
    public string Id { get; init; } = "singleton";

    /// <summary>UTC-время последнего успешного polling.</summary>
    public DateTime LastPollTimestamp { get; init; }

    /// <summary>Сколько задач обработано в последнем цикле.</summary>
    public int LastProcessedCount { get; init; }

    /// <summary>UTC-время последнего обновления.</summary>
    public DateTime UpdatedAt { get; init; }
}