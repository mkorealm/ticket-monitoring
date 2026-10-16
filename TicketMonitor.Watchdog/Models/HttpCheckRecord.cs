using LiteDB;

namespace TicketMonitor.Watchdog.Models;

/// <summary>Сырая запись HTTP-проверки. Пишется только при неудаче.</summary>
public sealed class HttpCheckRecord
{
    /// <summary>Уникальный ID.</summary>
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Имя сервиса.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Время проверки (UTC).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>HTTP-статус, если удалось получить ответ.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Время ответа (мс).</summary>
    public double? ResponseTimeMs { get; set; }

    /// <summary>Текст ошибки.</summary>
    public string? ErrorMessage { get; set; }
}