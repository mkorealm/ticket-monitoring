using LiteDB;

namespace TicketMonitor.Watchdog.Models;

/// <summary>Событие падения сервиса.</summary>
public sealed class DowntimeEvent
{
    /// <summary>Уникальный ID.</summary>
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Имя сервиса.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Время начала падения (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Время восстановления (UTC). <c>null</c>, если ещё лежит.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Длительность падения (мин). <c>null</c>, если ещё лежит.</summary>
    public double? DurationMinutes { get; set; }

    /// <summary>Причина (последняя ошибка).</summary>
    public string? Reason { get; set; }
}