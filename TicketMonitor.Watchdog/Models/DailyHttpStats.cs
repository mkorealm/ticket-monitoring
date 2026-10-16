using LiteDB;

namespace TicketMonitor.Watchdog.Models;

/// <summary>Агрегат HTTP-проверок за день по сервису.</summary>
public sealed class DailyHttpStats
{
    /// <summary>Ключ: <c>{yyyy-MM-dd}_{ServiceName}</c>.</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>Дата (без времени, UTC).</summary>
    public DateTime Date { get; set; }

    /// <summary>Имя сервиса.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Всего проверок.</summary>
    public long TotalChecks { get; set; }

    /// <summary>Неудачных проверок.</summary>
    public long FailedChecks { get; set; }

    /// <summary>Суммарное время ответа (мс) по успешным проверкам.</summary>
    public double TotalResponseTimeMs { get; set; }

    /// <summary>Суммарное время простоя (мин).</summary>
    public double TotalDowntimeMinutes { get; set; }
}