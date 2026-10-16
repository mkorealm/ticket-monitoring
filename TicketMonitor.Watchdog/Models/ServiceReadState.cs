using LiteDB;

namespace TicketMonitor.Watchdog.Models;

/// <summary>Состояние чтения operations.db конкретного сервиса.</summary>
public sealed class ServiceReadState
{
    /// <summary>Имя сервиса (первичный ключ).</summary>
    [BsonId]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Время последней прочитанной операции (UTC).</summary>
    public DateTime LastProcessedStartedAt { get; set; }
}