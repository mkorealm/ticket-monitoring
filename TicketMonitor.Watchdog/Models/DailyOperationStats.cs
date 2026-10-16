using LiteDB;

namespace TicketMonitor.Watchdog.Models;

/// <summary>Агрегат операций за день по сервису + типу + статусу.</summary>
public sealed class DailyOperationStats
{
    /// <summary>Ключ: <c>{yyyy-MM-dd}_{ServiceName}_{Type}_{Status}</c>.</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>Дата (без времени, UTC).</summary>
    public DateTime Date { get; set; }

    /// <summary>Имя сервиса.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Тип операции (например, <c>"GLPI:FetchTicket"</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Статус: <c>"Success"</c>, <c>"Failed"</c>, <c>"Cancelled"</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Количество операций.</summary>
    public long Count { get; set; }

    /// <summary>Суммарная длительность (мс).</summary>
    public double TotalDurationMs { get; set; }
}